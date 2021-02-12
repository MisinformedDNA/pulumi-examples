using System;
using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNextGen.Authorization.Latest;
using Pulumi.AzureNextGen.Authorization.Latest.Inputs;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview.Inputs;
using Pulumi.AzureNextGen.Insights.V20180501Preview;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest.Inputs;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Web.Latest;
using Pulumi.AzureNextGen.Web.Latest.Inputs;
using Pulumi.Random;
using ManagedServiceIdentityType = Pulumi.AzureNextGen.Web.Latest.ManagedServiceIdentityType;
using StorageSkuArgs = Pulumi.AzureNextGen.Storage.Latest.Inputs.SkuArgs;
using VaultSkuArgs = Pulumi.AzureNextGen.KeyVault.Latest.Inputs.SkuArgs;
using VaultSkuName = Pulumi.AzureNextGen.KeyVault.Latest.SkuName;

internal class AppStack : Stack
{
    private const string DefaultResourceGroupName = "credrotate2";

    public AppStack()
    {
        var config = new Config();
        var resourceGroupName = config.Get("resourceGroupName") ?? DefaultResourceGroupName;
        var resourceNamePrefix = config.Get("resourceNamePrefix") ?? resourceGroupName;
        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var (vault, resourceGroup, storageAccount1, storageAccount2) = AddInitalResources(resourceGroupName, resourceNamePrefix, tenantId);

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
            Sku = new StorageSkuArgs { Name = "Standard_LRS" },
        });

        var storageAccount2 = new StorageAccount("appStorageAccount2", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage2"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new StorageSkuArgs { Name = "Standard_LRS" },
        });

        var vault = new Vault("vault", new VaultArgs
        {
            Location = location,
            Properties = new VaultPropertiesArgs
            {
                AccessPolicies =
                {
                    new AccessPolicyEntryArgs
                    {
                         ObjectId = Output.Create(GetClientConfig.InvokeAsync()).Apply(c => c.ObjectId),
                         TenantId = tenantId,
                         Permissions = new PermissionsArgs
                         {
                             Secrets = { SecretPermissions.Delete }
                         }
                    }
                },
                EnabledForDeployment = false,
                EnabledForDiskEncryption = false,
                EnabledForTemplateDeployment = false,
                Sku = new VaultSkuArgs
                {
                    Family = "A",
                    Name = VaultSkuName.Standard,
                },
                TenantId = tenantId,
                EnableSoftDelete = false,  // This should be enabled for production
            },
            ResourceGroupName = resourceGroup.Name,
            VaultName = Output.Format($"{resourceNamePrefix}-kv"),
        });

        return (vault, resourceGroup, storageAccount1, storageAccount2);
    }

    private static void AddRotationFunction(Vault keyVault, ResourceGroup resourceGroup, StorageAccount storageAccount)
    {
        var config = new Config();
        var resourceGroupName = config.Get("resourceGroupName") ?? DefaultResourceGroupName;
        var functionAppName = config.Get("functionAppName") ?? $"{resourceGroupName}-storagekey-rotation-fnapp";

        var appServicePlanType = config.Get("appServicePlanType") ?? "Consumption Plan";
        var appServiceSku = appServicePlanType == "Consumption Plan" ? "Y1" : "P1V2";

        var appInsights = new Component("appInsights", new ComponentArgs
        {
            Location = resourceGroup.Location,
            RequestSource = "IbizaWebAppExtensionCreate",
            ResourceGroupName = resourceGroupName,
            ResourceName = functionAppName,
            Kind = "web",
            ApplicationType = "web",
        });

        var secretName = config.Get("secretName") ?? "storageKey2";
        var eventSubscriptionName = $"{functionAppName}-{secretName}";

        var prefix = new RandomString("randomPrefix", new RandomStringArgs { Special = false, Length = 13, Upper = false }).Result;

        var functionStorageAccountName = Output.Format($"{prefix}fnappstrg");
        var keyVaultName = config.Get("keyVaultName") ?? $"{resourceGroupName}-kv";
        var keyVaultRG = config.Get("keyVaultRG") ?? resourceGroupName;

        var repoURL = config.Get("repoURL") ?? "https://github.com/Azure-Samples/KeyVault-Rotation-StorageAccountKey-PowerShell.git";
        var serverFarm = new AppServicePlan("appServicePlan", new AppServicePlanArgs
        {
            Location = resourceGroup.Location,
            Name = $"{resourceGroupName}-rotation-fnapp-plan",
            ResourceGroupName = resourceGroupName,
            Sku = new SkuDescriptionArgs
            {
                Name = appServiceSku,
            },
        });

        var functionAppStorage = new StorageAccount("functionApp-storage", new StorageAccountArgs
        {
            AccountName = functionStorageAccountName,
            Kind = "Storage",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroupName,
            Sku = new StorageSkuArgs
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
            Name = functionAppName,
            ResourceGroupName = resourceGroupName,
            ServerFarmId = serverFarm.Id,
            Identity = new ManagedServiceIdentityArgs { Type = ManagedServiceIdentityType.SystemAssigned },
            SiteConfig = new SiteConfigArgs
            {
                AppSettings =
                {
                    new NameValuePairArgs
                    {
                        Name = "AzureWebJobsStorage",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={functionStorageAccountName};AccountKey={functionAppStorageKey}"),
                    },
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_EXTENSION_VERSION",
                        Value = "~3",
                    },
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_WORKER_RUNTIME",
                        Value = "powershell",
                    },
                    new NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={functionStorageAccountName};EndpointSuffix=core.windows.net;AccountKey={functionAppStorageKey}"),
                    },
                    new NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTSHARE",
                        Value = functionAppName.ToLower(),
                    },
                    new NameValuePairArgs
                    {
                        Name = "WEBSITE_NODE_DEFAULT_VERSION",
                        Value = "~10",
                    },
                    new NameValuePairArgs
                    {
                        Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                        Value = appInsights.InstrumentationKey,
                    },
                },
            },
        });

        var functionAppSourceControl = new WebAppSourceControl("functionAppSourceControl",
            new WebAppSourceControlArgs
            {
                Name = functionApp.Name,
                RepoUrl = repoURL,
                Branch = "main",
                IsManualIntegration = true,
                ResourceGroupName = resourceGroupName,
            });

        var eventSubscription = KvEventSubscriptionAndGrantAccess(keyVault, functionApp, functionAppSourceControl, resourceGroup, secretName, "SecretExpiry");

        var expiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        _ = new Secret("secret",
            new SecretArgs
            {
                ResourceGroupName = resourceGroup.Name,
                SecretName = secretName,
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
                    RoleAssignmentName = new RandomUuid("roleId", new RandomUuidArgs()).Result,
                    Properties = new RoleAssignmentPropertiesArgs
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
                TenantId = functionApp.Identity.Apply(i => i!.TenantId),
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
