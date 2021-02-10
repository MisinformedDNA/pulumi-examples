// Copyright 2016-2020, Pulumi Corporation.  All rights reserved.

using System;
using Pulumi;
using Pulumi.AzureNextGen.Authorization.Latest;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview.Inputs;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview;
using Pulumi.AzureNextGen.Insights.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest.Inputs;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Sql.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Web.Latest;
using Pulumi.AzureNextGen.Web.Latest.Inputs;
using KeyVault = Pulumi.AzureNextGen.KeyVault.Latest;
using Storage = Pulumi.AzureNextGen.Storage.Latest;
using ManagedServiceIdentityType = Pulumi.AzureNextGen.Web.Latest.ManagedServiceIdentityType;

internal class AppStack : Stack
{
    [Output]
    public Output<string> WebAppEndpoint { get; set; }

    public AppStack()
    {
        var config = new Config();
        var resourceGroupName = config.Get("resourceGroupName") ?? "rotatesecretoneset";
        var resourceNamePrefix = config.Get("resourceNamePrefix") ?? resourceGroupName;
        var location = config.Get("location") ?? "EastUS";

        var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs { ResourceGroupName = resourceGroupName, Location = location });

        var sqlAdminLogin = config.Get("sqlAdminLogin") ?? "sqlAdmin";
        var sqlAdminPassword = Guid.NewGuid().ToString();
        var sqlServer = new Server("sqlServer", new ServerArgs
        {
            AdministratorLogin = sqlAdminLogin,
            AdministratorLoginPassword = sqlAdminPassword,
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            ServerName = $"{resourceNamePrefix}-sql",
            Version = "12.0",
        });

        new Pulumi.Azure.Sql.FirewallRule("firewallRule",
            new Pulumi.Azure.Sql.FirewallRuleArgs
            {
                Name = "AllowAllWindowsAzureIps",
                ServerName = sqlServer.Name,
                ResourceGroupName = resourceGroup.Name,
                StartIpAddress = "0.0.0.0",
                EndIpAddress = "0.0.0.0",
            });

        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var storageAccountName = $"st{resourceNamePrefix}";
        var storageAccount = new StorageAccount("storageAccount", new StorageAccountArgs
        {
            AccountName = storageAccountName,
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs
            {
                Name = "Standard_LRS"
            },
        });

        var functionAppName = $"{resourceNamePrefix}-func";
        var appInsights = new Component("appInsights", new ComponentArgs
        {
            Location = location,
            RequestSource = "IbizaWebAppExtensionCreate",
            ResourceGroupName = resourceGroup.Name,
            ResourceName = functionAppName,
            ApplicationType = "web",
            Kind = "web",
            Tags =
            {
                //{ "[concat('hidden-link:', resourceId('Microsoft.Web/sites', parameters('functionAppName')))]", "Resource" },
            },
        });

        var secretName = config.Get("secretName") ?? "sqlPassword";

        var appService = new AppServicePlan("functionApp-appService", new AppServicePlanArgs
        {
            Location = location,
            Name = functionAppName,
            ResourceGroupName = resourceGroup.Name,
            Sku = new SkuDescriptionArgs
            {
                Name = "Y1",
                Tier = "Dynamic",
            },
        });

        var storageKey = storageAccount.Name.Apply(a =>
        {
            var task = ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs { AccountName = a, ResourceGroupName = resourceGroupName });
            return Output.Create(task).Apply(t => t.Keys[0].Value);
        });

        var functionApp = new WebApp("functionApp", new WebAppArgs
        {
            Kind = "functionapp",
            Name = functionAppName,
            ResourceGroupName = resourceGroup.Name,
            ServerFarmId = appService.Id,
            Location = resourceGroup.Location,
            Identity = new ManagedServiceIdentityArgs { Type = ManagedServiceIdentityType.SystemAssigned },
            SiteConfig = new SiteConfigArgs
            {
                AppSettings =
                {
                    new NameValuePairArgs
                    {
                        Name = "AzureWebJobsStorage",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageKey}"),
                    },
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_EXTENSION_VERSION",
                        Value = "~3",
                    },
                    new NameValuePairArgs
                    {
                        Name = "FUNCTIONS_WORKER_RUNTIME",
                        Value = "dotnet",
                    },
                    new NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING",
                        Value = Output.Format($"DefaultEndpointsProtocol=https;AccountName={storageAccountName};EndpointSuffix=core.windows.net;AccountKey={storageKey}"),
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

        var functionAppSourceControl = new WebAppSourceControl("functionApp-sourceControl",
            new WebAppSourceControlArgs
            {
                Name = functionApp.Name,
                IsManualIntegration = true,
                Branch = "main",
                RepoUrl = config.Get("functionAppRepoURL") ?? "https://github.com/Azure-Samples/KeyVault-Rotation-SQLPassword-Csharp.git",
                ResourceGroupName = resourceGroup.Name,
            });

        var keyVault = new Vault("keyVault", new VaultArgs
        {
            Location = location,
            Properties = new VaultPropertiesArgs
            {
                AccessPolicies = { new AccessPolicyEntryArgs
                {
                    TenantId = tenantId,
                    ObjectId = functionApp.Identity.Apply(i => i!.PrincipalId),
                    Permissions = new PermissionsArgs
                    {
                        Secrets = { "get", "list", "set" },
                    },
                }
                },
                EnabledForDeployment = false,
                EnabledForDiskEncryption = false,
                EnabledForTemplateDeployment = false,
                Sku = new SkuArgs
                {
                    Family = "A",
                    Name = KeyVault.SkuName.Standard,
                },
                TenantId = tenantId,
                EnableSoftDelete = false,
            },
            ResourceGroupName = resourceGroup.Name,
            VaultName = $"{resourceNamePrefix}-kv",
        });

        var topicName = "SecretExpiry";
        var topic = new SystemTopic("eventGridTopic",
            new SystemTopicArgs
            {
                SystemTopicName = topicName,
                Source = keyVault.Id,
                TopicType = "microsoft.keyvault.vaults",
                Location = resourceGroup.Location,
                ResourceGroupName = resourceGroup.Name,
            });

        var eventSubscription = new SystemTopicEventSubscription("eventSubscription",
            new SystemTopicEventSubscriptionArgs
            {
                EventSubscriptionName = Output.Format($"{keyVault.Name}-{secretName}-{functionAppName}"),
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
                    ResourceId = Output.Format($"{functionApp.Id}/functions/AKVSQLRotation"),
                    EndpointType = "AzureFunction",
                    MaxEventsPerBatch = 1,
                    PreferredBatchSizeInKilobytes = 64,
                },
            },
            new CustomResourceOptions { DependsOn = { functionAppSourceControl } });

        var expiresAt = DateTimeOffset.Now.AddMinutes(1).ToUnixTimeSeconds();
        var secret = new Secret("secret",
            new SecretArgs
            {
                SecretName = secretName,
                VaultName = keyVault.Name,
                ResourceGroupName = resourceGroup.Name,
                Tags = { { "CredentialId", sqlAdminLogin }, { "ProviderAddress", sqlServer.Id }, { "ValidityPeriodDays", "1" } },
                Properties = new SecretPropertiesArgs
                {
                    Value = sqlAdminPassword,
                    Attributes = new SecretAttributesArgs { Expires = (int)expiresAt },
                },
            },
            new CustomResourceOptions { DependsOn = { eventSubscription } }); 

        var webAppAppService = new AppServicePlan("webApp-appService", new AppServicePlanArgs
        {
            Location = location,
            Name = $"{resourceNamePrefix}-app",
            ResourceGroupName = resourceGroup.Name,
            Sku = new SkuDescriptionArgs
            {
                Name = "F1",
            },
        });

        var webApp = new WebApp("webApp", new WebAppArgs
        {
            Kind = "app",
            Name = $"{resourceNamePrefix}-app",
            ResourceGroupName = resourceGroup.Name,
            ServerFarmId = webAppAppService.Id,
            Location = location,
            Identity = new ManagedServiceIdentityArgs { Type = ManagedServiceIdentityType.SystemAssigned },
            SiteConfig = new SiteConfigArgs
            {
                AppSettings =
                {
                    new NameValuePairArgs
                    {
                        Name = "DataSource",
                        Value = Output.Format($"{sqlServer.Name}.database.windows.net"),
                    },
                    new NameValuePairArgs
                    {
                        Name = "KeyVaultName",
                        Value = keyVault.Name,
                    },
                    new NameValuePairArgs
                    {
                        Name = "SecretName",
                        Value = secretName,
                    },
                },
            },
        });

        this.WebAppEndpoint = webApp.DefaultHostName;

        new WebAppSourceControl("webApp-sourceControl",
            new WebAppSourceControlArgs
            {
                Name = webApp.Name,
                IsManualIntegration = true,
                Branch = "main",
                RepoUrl = config.Get("webAppRepoURL") ?? "https://github.com/Azure-Samples/KeyVault-Rotation-SQLPassword-Csharp-WebApp.git",
                ResourceGroupName = resourceGroup.Name,
            });

        var accessPolicyResource = new Pulumi.Azure.KeyVault.AccessPolicy("webAppPolicy",
            new Pulumi.Azure.KeyVault.AccessPolicyArgs
            {
                KeyVaultId = keyVault.Id,
                TenantId = tenantId,
                ObjectId = webApp.Identity.Apply(i => i!.PrincipalId),
                SecretPermissions = { "get", "list", "set" }
            });
    }
}
