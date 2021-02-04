using System;
using Pulumi;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview.Inputs;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Web.Latest;
using Pulumi.Random;
using AzureNextGen = Pulumi.AzureNextGen;

class FunctionStack //: Stack
{
//    public FunctionStack(Vault keyVault, WebApp functionApp, ResourceGroup resourceGroup, string secretName, string topicName)
    public FunctionStack(Vault keyVault, ResourceGroup resourceGroup)
    //public FunctionStack()
    {
        var config = new Config();
        var resourceGroupNameParam = config.Require("resourceGroupNameParam");
        //var resourceGroupVar = Output.Create(GetResourceGroup.InvokeAsync(new GetResourceGroupArgs
        //{
        //    ResourceGroupName = resourceGroupNameParam,
        //}));

        var resourceGroupVar = Output.Create(resourceGroup);

        //var resourceGroupVar = Output.Create(new ResourceGroup("sad", new ResourceGroupArgs()));

        var functionAppNameParam = config.Get("functionAppNameParam") is string name
            ? Output.Create(name)
            : resourceGroupVar.Apply(r => Output.Format($"{r.Name}-storagekey-rotation-fnapp"));

        var appServicePlanType = config.Get("appServicePlanType") ?? "Consumption Plan";
        var appServiceSku = appServicePlanType == "Consumption Plan" ? "Y1" : "P1V2";

        var componentResource = new AzureNextGen.Insights.V20180501Preview.Component("componentResource", new AzureNextGen.Insights.V20180501Preview.ComponentArgs
        {
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            RequestSource = "IbizaWebAppExtensionCreate",
            ResourceGroupName = resourceGroupNameParam,
            ResourceName = functionAppNameParam,
            Kind = "web",
            ApplicationType = "web",
            Tags =
            {
                //{ "[concat('hidden-link:', resourceId('Microsoft.Web/sites', parameters('functionAppName')))]", "Resource" },
            },
        });
        var secretNameParam = config.Get("secretNameParam") ?? "storageKey";
        var eventSubscriptionNameVar = $"{functionAppNameParam}-{secretNameParam}";

        var prefix = new RandomString("randomPrefix", new RandomStringArgs { Special = false, Length = 13, Upper = false }).Result;

        var functionStorageAccountNameVar = Output.Format($"{prefix}fnappstrg");
        //var keyVaultNameParam = Output.Create(config.Get("keyVaultNameParam")) ?? resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-kv");
        var keyVaultNameParam = config.Get("keyVaultNameParam") is string k ? Output.Create(k) : resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-kv");
        var keyVaultRGParam = Output.Create(config.Get("keyVaultRGParam")) ?? resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Name);

        var repoURLParam = config.Get("repoURLParam") ?? "https://github.com/Azure-Samples/KeyVault-Rotation-StorageAccountKey-PowerShell.git";
        var serverfarmResource = new AzureNextGen.Web.V20180201.AppServicePlan("serverfarmResource", new AzureNextGen.Web.V20180201.AppServicePlanArgs
        {
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            Name = resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-rotation-fnapp-plan"),
            ResourceGroupName = resourceGroupNameParam,
            Sku = new AzureNextGen.Web.V20180201.Inputs.SkuDescriptionArgs
            {
                Name = appServiceSku,
            },
        });
        var siteResource = new AzureNextGen.Web.Latest.WebApp("siteResource", new AzureNextGen.Web.Latest.WebAppArgs
        {
            Enabled = true,
            Kind = "functionapp",
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            Name = functionAppNameParam,
            ResourceGroupName = resourceGroupNameParam,
            SiteConfig = new AzureNextGen.Web.Latest.Inputs.SiteConfigArgs
            {
                AppSettings =
                {
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "AzureWebJobsStorage",
                        Value = "[concat('DefaultEndpointsProtocol=https;AccountName=', variables('functionStorageAccountName'), ';AccountKey=', listKeys(resourceId('Microsoft.Storage/storageAccounts', variables('functionStorageAccountName')), '2019-06-01').keys[0].value)]",
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
                        Value = "[concat('DefaultEndpointsProtocol=https;AccountName=', variables('functionStorageAccountName'), ';EndpointSuffix=', environment().suffixes.storage, ';AccountKey=',listKeys(resourceId('Microsoft.Storage/storageAccounts', variables('functionStorageAccountName')), '2019-06-01').keys[0].value)]",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTSHARE",
                        Value = "[toLower(parameters('functionAppName'))]",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_NODE_DEFAULT_VERSION",
                        Value = "~10",
                    },
                    new AzureNextGen.Web.Latest.Inputs.NameValuePairArgs
                    {
                        Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                        Value = "[reference(resourceId('microsoft.insights/components', parameters('functionAppName')), '2018-05-01-preview').InstrumentationKey]",
                    },
                },
            },
        });

        var sourcecontrolResource = new WebAppSourceControl("sourceControl",
            new WebAppSourceControlArgs
            {
                Name = siteResource.Name,
                RepoUrl = repoURLParam,
                Branch = "main",
                IsManualIntegration = true,
                ResourceGroupName = resourceGroupNameParam,
            });

        var storageAccountNameParam = Output.Create(config.Get("storageAccountNameParam")) ?? resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}storage");
        var storageAccountRGParam = Output.Create(config.Get("storageAccountRGParam")) ?? resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Name);
        var storageAccountResource = new AzureNextGen.Storage.V20190601.StorageAccount("storageAccountResource", new AzureNextGen.Storage.V20190601.StorageAccountArgs
        {
            AccountName = functionStorageAccountNameVar,
            Kind = "Storage",
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            ResourceGroupName = resourceGroupNameParam,
            Sku = new AzureNextGen.Storage.V20190601.Inputs.SkuArgs
            {
                Name = "Standard_LRS",
            },
        });

        KvEventSubscriptionAndGrantAccess(keyVault, siteResource, resourceGroup, secretNameParam, "SecretExpiry");
        //var _ = Output.Tuple(keyVaultNameParam, functionAppNameParam).Apply(t => KvEventSubscriptionAndGrantAccess(t.Item1, t.Item2, resourceGroupNameParam, secretNameParam, "SecretExpiry"));
        //var _ = Output.Tuple(keyVaultNameParam, functionAppNameParam).Apply(t => new KvEventSubscriptionAndGrantAccessComponent("eventSub", t.Item1, t.Item2, resourceGroupNameParam, secretNameParam, "SecretExpiry"));
        StorageGrantAccess();


        static void StorageGrantAccess()
        {
            //TODO:
            //        {
            //            "type": "Microsoft.Storage/storageAccounts/providers/roleAssignments",
            //                        "apiVersion": "2018-09-01-preview",
            //                        "name": "[concat(parameters('storageAccountName'), '/Microsoft.Authorization/', guid(concat(parameters('storageAccountName'),reference(resourceId('Microsoft.Web/sites', parameters('functionAppName')),'2019-08-01', 'Full').identity.principalId)))]",
            //                        "properties": {
            //        "roleDefinitionId": "[concat('/subscriptions/', subscription().subscriptionId, '/providers/Microsoft.Authorization/roleDefinitions/', '81a9662b-bebf-436f-a333-f67b29880f12')]",
            //                            "principalId": "[reference(resourceId('Microsoft.Web/sites', parameters('functionAppName')),'2019-08-01', 'Full').identity.principalId]"
            //                        }
            //}
        }
    }

    static void KvEventSubscriptionAndGrantAccess(Vault keyVault, WebApp functionApp, ResourceGroup resourceGroup, string secretName, string topicName) // TODO: Consider moving this to a component
    {
        new Pulumi.Azure.KeyVault.AccessPolicy("accessPolicy",
            new Pulumi.Azure.KeyVault.AccessPolicyArgs
            {
                ObjectId = functionApp.Identity.Apply(i => i.PrincipalId),
                TenantId = functionApp.Identity.Apply(i => i.TenantId),    // TODO: use GetClientConfig?
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
                    ResourceId = Output.Format($"{functionApp.Id}/functions/AKVSQLRotation"),
                    EndpointType = "AzureFunction",
                    MaxEventsPerBatch = 1,
                    PreferredBatchSizeInKilobytes = 64,
                },
            });
    }

    //static void KvEventSubscriptionAndGrantAccess2(string keyVaultName, string functionAppName, string resourceGroupName, string secretName, string topicName) // TODO: Consider moving this to a component
    //{
    //    var resourceGroup = Output.Create(GetResourceGroup.InvokeAsync(new GetResourceGroupArgs { ResourceGroupName = resourceGroupName }));
    //    var keyVault = Output.Create(GetVault.InvokeAsync(new GetVaultArgs { VaultName = keyVaultName, ResourceGroupName = resourceGroupName }));
    //    var functionApp = Output.Create(GetWebApp.InvokeAsync(new GetWebAppArgs { Name = functionAppName, ResourceGroupName = resourceGroupName }));

    //    var tuple = Output.Tuple(keyVault, functionApp, resourceGroup)
    //        .Apply(t => KvEventSubscriptionAndGrantAccess(t.Item1, t.Item2, t.Item3, secretName, topicName);)
    //        //return KvEventSubscriptionAndGrantAccess(keyVault, functionApp, resourceGroup, secretName, topicName);

    //    }

}
