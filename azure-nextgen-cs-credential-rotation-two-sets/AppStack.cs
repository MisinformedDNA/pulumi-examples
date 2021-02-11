using System;
using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNextGen.Authorization.Latest;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview.Inputs;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest.Inputs;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Web.Latest;
using Pulumi.Random;
using AzureNextGen = Pulumi.AzureNextGen;
using KeyVault = Pulumi.AzureNextGen.KeyVault.Latest;
using Storage = Pulumi.AzureNextGen.Storage.Latest;

internal class AppStack : Stack
{
    public AppStack()
    {
        var config = new Config();
        var resourceGroupName = config.Get("resourceGroupName") ?? "credrotate2";
        var resourceNamePrefixParam = config.Get("resourceNamePrefix") ?? resourceGroupName;
        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var (vault, resourceGroup, storageAccount1, storageAccount2) = AddInitalResources(resourceGroupName, resourceNamePrefixParam, tenantId);

        AddRotationFunction(vault, resourceGroup, storageAccount1);
    }

    private static (Vault, ResourceGroup, StorageAccount, StorageAccount) AddInitalResources(string resourceGroupName, string? resourceNamePrefix, Output<string> tenantId)
    {
        var location = "CentralUS";
        var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs { ResourceGroupName = resourceGroupName, Location = location });

        resourceNamePrefix ??= resourceGroupName;

        var storageAccount1 = new StorageAccount("appStorageAccount", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        var storageAccount2 = new StorageAccount("appStorageAccount2", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage2"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        var vault = new Vault("vault", new VaultArgs
        {
            Location = location,
            Properties = new VaultPropertiesArgs
            {
                AccessPolicies = new List<AccessPolicyEntryArgs>(0),
                EnabledForDeployment = false,
                EnabledForDiskEncryption = false,
                EnabledForTemplateDeployment = false,
                Sku = new KeyVault.Inputs.SkuArgs
                {
                    Family = "A",
                    Name = KeyVault.SkuName.Standard,
                },
                TenantId = tenantId,
            },
            ResourceGroupName = resourceGroup.Name,
            VaultName = Output.Format($"{resourceNamePrefix}-kv"),
        });

        return (vault, resourceGroup, storageAccount1, storageAccount2);
    }

    private static void AddRotationFunction(Vault keyVault, ResourceGroup resourceGroup, StorageAccount storageAccount)
    {
        var config = new Config();
        var resourceGroupName = config.Get("resourceGroupName") ?? "credrotate2";
        var functionAppNameParam = config.Get("functionAppNameParam") ?? $"{resourceGroupName}-storagekey-rotation-fnapp";

        var appServicePlanType = config.Get("appServicePlanType") ?? "Consumption Plan";
        var appServiceSku = appServicePlanType == "Consumption Plan" ? "Y1" : "P1V2";

        var componentResource = new AzureNextGen.Insights.V20180501Preview.Component("componentResource", new AzureNextGen.Insights.V20180501Preview.ComponentArgs
        {
            Location = resourceGroup.Location,
            RequestSource = "IbizaWebAppExtensionCreate",
            ResourceGroupName = resourceGroupName,
            ResourceName = functionAppNameParam,
            Kind = "web",
            ApplicationType = "web",
            Tags =
            {
            },
        });

        var secretNameParam = config.Get("secretNameParam") ?? "storageKey";
        var eventSubscriptionNameVar = $"{functionAppNameParam}-{secretNameParam}";

        var prefix = new RandomString("randomPrefix", new RandomStringArgs { Special = false, Length = 13, Upper = false }).Result;

        var functionStorageAccountNameVar = Output.Format($"{prefix}fnappstrg");
        var keyVaultNameParam = config.Get("keyVaultNameParam") ?? $"{resourceGroupName}-kv";
        var keyVaultRGParam = config.Get("keyVaultRGParam") ?? resourceGroupName;

        var repoURLParam = config.Get("repoURLParam") ?? "https://github.com/MisinformedDNA/keyvault-rotation-storageaccountkey-powershell/" ?? "https://github.com/Azure-Samples/KeyVault-Rotation-StorageAccountKey-PowerShell.git";
        var serverfarmResource = new AzureNextGen.Web.V20180201.AppServicePlan("serverfarmResource", new AzureNextGen.Web.V20180201.AppServicePlanArgs
        {
            Location = resourceGroup.Location,
            Name = $"{resourceGroupName}-rotation-fnapp-plan",
            ResourceGroupName = resourceGroupName,
            Sku = new AzureNextGen.Web.V20180201.Inputs.SkuDescriptionArgs
            {
                Name = appServiceSku,
            },
        });

        var functionAppStorage = new StorageAccount("functionApp-storage", new StorageAccountArgs
        {
            AccountName = functionStorageAccountNameVar,
            Kind = "Storage",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroupName,
            Sku = new Storage.Inputs.SkuArgs
            {
                Name = "Standard_LRS",
            },
        });

        var functionAppStorageKey = GetFirstStorageAccountKey(functionAppStorage, resourceGroupName);
        var functionApp = new WebApp("functionApp", new WebAppArgs
        {
            Enabled = true,
            Kind = "functionapp",
            Location = resourceGroup.Location,
            Name = functionAppNameParam,
            ResourceGroupName = resourceGroupName,
            ServerFarmId = serverfarmResource.Id,
            Identity = new AzureNextGen.Web.Latest.Inputs.ManagedServiceIdentityArgs { Type = AzureNextGen.Web.Latest.ManagedServiceIdentityType.SystemAssigned },
            SiteConfig = new AzureNextGen.Web.Latest.Inputs.SiteConfigArgs
            {
                AppSettings =
                {
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "AzureWebJobsStorage",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={functionStorageAccountNameVar};AccountKey={functionAppStorageKey}"),
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "FUNCTIONS_EXTENSION_VERSION",
                        Value = "~3",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "FUNCTIONS_WORKER_RUNTIME",
                        Value = "powershell",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={functionStorageAccountNameVar};EndpointSuffix=core.windows.net;AccountKey={functionAppStorageKey}"),
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTSHARE",
                        Value = functionAppNameParam.ToLower(),
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_NODE_DEFAULT_VERSION",
                        Value = "~10",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                        Value = componentResource.InstrumentationKey,
                    },
                },
            },
        });

        var functionAppSourceControl = new WebAppSourceControl("functionAppSourceControl",
            new WebAppSourceControlArgs
            {
                Name = functionApp.Name,
                RepoUrl = repoURLParam,
                Branch = "main",
                IsManualIntegration = true,
                ResourceGroupName = resourceGroupName,
            });

        var eventSubscription = KvEventSubscriptionAndGrantAccess(keyVault, functionApp, functionAppSourceControl, resourceGroup, secretNameParam, "SecretExpiry");

        var expiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        _ = new Secret("secret",
            new SecretArgs
            {
                ResourceGroupName = resourceGroup.Name,
                SecretName = "storageKey",
                VaultName = keyVault.Name,
                Properties = new SecretPropertiesArgs
                {
                    Value = GetFirstStorageAccountKey(storageAccount, resourceGroupName),
                    Attributes = new SecretAttributesArgs { Expires = (int)expiresAt },
                },
                Tags = { { "CredentialId", "key1" }, { "ProviderAddress", storageAccount.Id }, { "ValidityPeriodDays", "60" } },
            },
            new CustomResourceOptions { DependsOn = { eventSubscription } });

        StorageGrantAccess();

        void StorageGrantAccess()
        {
            new RoleAssignment("functionAppAccessToStorage",
                new RoleAssignmentArgs
                {
                    RoleAssignmentName = Guid.NewGuid().ToString(),
                    Properties = new AzureNextGen.Authorization.Latest.Inputs.RoleAssignmentPropertiesArgs
                    {
                        PrincipalId = functionApp.Identity.Apply(f => f!.PrincipalId),
                        RoleDefinitionId = "/subscriptions/1788357e-d506-4118-9f88-092c1dcddc16/providers/Microsoft.Authorization/roleDefinitions/81a9662b-bebf-436f-a333-f67b29880f12",
                    },
                    Scope = storageAccount.Id,
                },
                new CustomResourceOptions { DependsOn = { storageAccount, functionApp } });

        }
    }

    private static SystemTopicEventSubscription KvEventSubscriptionAndGrantAccess(Vault keyVault, WebApp functionApp, WebAppSourceControl functionAppSourceControl, ResourceGroup resourceGroup, string secretName, string topicName) // TODO: Consider moving this to a component
    {
        new Pulumi.Azure.KeyVault.AccessPolicy("accessPolicy",
            new Pulumi.Azure.KeyVault.AccessPolicyArgs
            {
                ObjectId = functionApp.Identity.Apply(i => i!.PrincipalId),
                TenantId = functionApp.Identity.Apply(i => i!.TenantId),    // TODO: use GetClientConfig?
                SecretPermissions = { "get", "set", "list" },
                KeyVaultId = keyVault.Id,
            });

        var topic = new SystemTopic("eventGridTopic",
            new SystemTopicArgs
            {
                SystemTopicName = topicName,
                Source = keyVault.Id,
                TopicType = "microsoft.keyvault.vaults",
                Location = resourceGroup.Location,
                ResourceGroupName = resourceGroup.Name,
            });

        var eventSubscription = new SystemTopicEventSubscription("secretExpiryEvent",
            new SystemTopicEventSubscriptionArgs
            {
                EventSubscriptionName = Output.Format($"{keyVault.Name}-{secretName}-{functionApp.Name}"),
                SystemTopicName = topic.Name,
                ResourceGroupName = resourceGroup.Name,
                Filter = new EventSubscriptionFilterArgs
                {
                    SubjectBeginsWith = secretName,
                    SubjectEndsWith = secretName,
                    IncludedEventTypes = { "Microsoft.KeyVault.SecretNearExpiry" },
                },
                Destination = new AzureFunctionEventSubscriptionDestinationArgs
                {
                    ResourceId = Output.Format($"{functionApp.Id}/functions/AKVStorageRotation"),
                    EndpointType = "AzureFunction",
                    MaxEventsPerBatch = 1,
                    PreferredBatchSizeInKilobytes = 64,
                },
            },
            new CustomResourceOptions { DependsOn = { functionAppSourceControl } });

        return eventSubscription;
    }

    private static Output<string> GetFirstStorageAccountKey(StorageAccount storageAccount, string resourceGroupName)
    {
        return storageAccount.Name.Apply(s =>
        {
            return GetFirstStorageAccountKey(s, resourceGroupName);
        });
    }

    private static Output<string> GetFirstStorageAccountKey(string storageAccountName, string resourceGroupName)
    {
        var keys = Output.Create(ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs { AccountName = storageAccountName, ResourceGroupName = resourceGroupName }));
        return keys.Apply(k => k.Keys[0].Value);
    }
}
