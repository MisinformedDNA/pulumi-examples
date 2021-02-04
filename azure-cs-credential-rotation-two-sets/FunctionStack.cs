using Pulumi;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview;
using Pulumi.AzureNextGen.EventGrid.V20200401Preview.Inputs;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Web.Latest;
using AzureNextGen = Pulumi.AzureNextGen;

class FunctionStack : Stack
{
    public FunctionStack()
    {
        var config = new Config();
        var resourceGroupNameParam = config.Require("resourceGroupNameParam");
        var resourceGroupVar = Output.Create(GetResourceGroup.InvokeAsync(new GetResourceGroupArgs
        {
            ResourceGroupName = resourceGroupNameParam,
        }));
        var appServiceSKUVar = "[if(equals(parameters('appServicePlanType'),'Consumption Plan'),'Y1','P1V2')]";
        var appServiceSku = config.Get("appServicePlanType") == "Consumption Plan" ? "Y1" : "P1V2";
        var functionAppNameParam = Output.Create(config.Get("functionAppNameParam")) ?? resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-storagekey-rotation-fnapp");
        var componentResource = new AzureNextGen.Insights.V20180501Preview.Component("componentResource", new AzureNextGen.Insights.V20180501Preview.ComponentArgs
        {
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            RequestSource = "IbizaWebAppExtensionCreate",
            ResourceGroupName = resourceGroupNameParam,
            ResourceName = functionAppNameParam,
            Tags =
            {
                { "[concat('hidden-link:', resourceId('Microsoft.Web/sites', parameters('functionAppName')))]", "Resource" },
            },
        });
        var secretNameParam = config.Get("secretNameParam") ?? "storageKey";
        var eventSubscriptionNameVar = $"{functionAppNameParam}-{secretNameParam}";
        var functionStorageAccountNameVar = "[concat(uniquestring(parameters('functionAppName')), 'fnappstrg')]";
        var keyVaultNameParam = Output.Create(config.Get("keyVaultNameParam")) ?? resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-kv");
        var keyVaultRGParam = Output.Create(config.Get("keyVaultRGParam")) ?? resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Name);

        var _ = Output.Tuple(keyVaultNameParam, functionAppNameParam).Apply(t => KvEventSubscriptionAndGrantAccess(t.Item1, t.Item2, resourceGroupNameParam, secretNameParam, "SecretExpiry"));
        var repoURLParam = config.Get("repoURLParam") ?? "https://github.com/Azure-Samples/KeyVault-Rotation-StorageAccountKey-PowerShell.git";
        var serverfarmResource = new AzureNextGen.Web.V20180201.AppServicePlan("serverfarmResource", new AzureNextGen.Web.V20180201.AppServicePlanArgs
        {
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            Name = resourceGroupVar.Apply(resourceGroupVar => $"{resourceGroupVar.Name}-rotation-fnapp-plan"),
            ResourceGroupName = resourceGroupNameParam,
            Sku = new AzureNextGen.Web.V20180201.Inputs.SkuDescriptionArgs
            {
                Name = appServiceSKUVar,
            },
        });
        var siteResource = new AzureNextGen.Web.V20181101.WebApp("siteResource", new AzureNextGen.Web.V20181101.WebAppArgs
        {
            Enabled = true,
            Kind = "functionapp",
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            Name = functionAppNameParam,
            ResourceGroupName = resourceGroupNameParam,
            SiteConfig = new AzureNextGen.Web.V20181101.Inputs.SiteConfigArgs
            {
                AppSettings =
                {
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "AzureWebJobsStorage",
                        Value = "[concat('DefaultEndpointsProtocol=https;AccountName=', variables('functionStorageAccountName'), ';AccountKey=', listKeys(resourceId('Microsoft.Storage/storageAccounts', variables('functionStorageAccountName')), '2019-06-01').keys[0].value)]",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "FUNCTIONS_EXTENSION_VERSION",
                        Value = "~3",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "FUNCTIONS_WORKER_RUNTIME",
                        Value = "powershell",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING",
                        Value = "[concat('DefaultEndpointsProtocol=https;AccountName=', variables('functionStorageAccountName'), ';EndpointSuffix=', environment().suffixes.storage, ';AccountKey=',listKeys(resourceId('Microsoft.Storage/storageAccounts', variables('functionStorageAccountName')), '2019-06-01').keys[0].value)]",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_CONTENTSHARE",
                        Value = "[toLower(parameters('functionAppName'))]",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
                    {
                        Name = "WEBSITE_NODE_DEFAULT_VERSION",
                        Value = "~10",
                    },
                    new AzureNextGen.Web.V20181101.Inputs.NameValuePairArgs
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
                RepoUrl = repoURLParam,
                Branch = "main",
                IsManualIntegration = true,
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

        StorageGrantAccess();

        static bool KvEventSubscriptionAndGrantAccess(string keyVaultName, string functionAppName, string resourceGroupName, string secretName, string topicName = "SecretExpiry") // TODO: Consider moving this to a component
        {
            var resourceGroup = Output.Create(GetResourceGroup.InvokeAsync(new GetResourceGroupArgs { ResourceGroupName = resourceGroupName }));
            var keyVault = Output.Create(GetVault.InvokeAsync(new GetVaultArgs { VaultName = keyVaultName, ResourceGroupName = resourceGroupName }));
            var functionApp = Output.Create(GetWebApp.InvokeAsync(new GetWebAppArgs { Name = functionAppName, ResourceGroupName = resourceGroupName }));

            new Pulumi.Azure.KeyVault.AccessPolicy("accessPolicy",
                new Pulumi.Azure.KeyVault.AccessPolicyArgs
                {
                    ObjectId = functionApp.Apply(f => f.Identity).Apply(i => i.PrincipalId),
                    TenantId = functionApp.Apply(f => f.Identity).Apply(i => i.TenantId),    // TODO: use GetClientConfig?
                    SecretPermissions = { "get", "set", "list" },
                    KeyVaultId = keyVault.Apply(k => k.Id),
                });

            var topic = new SystemTopic("eventGridTopic",
                new SystemTopicArgs
                {
                    SystemTopicName = topicName,
                    Source = keyVault.Apply(k => k.Id),
                    TopicType = "microsoft.keyvault.vaults",
                    Location = resourceGroup.Apply(r => r.Location),
                    ResourceGroupName = resourceGroup.Apply(r => r.Name),
                });

            var eventSubscription = new SystemTopicEventSubscription("secretExpiryEvent",
                new SystemTopicEventSubscriptionArgs
                {
                    EventSubscriptionName = Output.Format($"{keyVaultName}-{secretName}-{functionAppName}"),
                    SystemTopicName = topic.Name,
                    ResourceGroupName = resourceGroup.Apply(r => r.Name),
                    Filter = new EventSubscriptionFilterArgs
                    {
                        SubjectBeginsWith = secretName,
                        SubjectEndsWith = secretName,
                        IncludedEventTypes = { "Microsoft.KeyVault.SecretNearExpiry" },
                    },
                    Destination = new AzureFunctionEventSubscriptionDestinationArgs
                    {
                        ResourceId = Output.Format($"{functionApp.Apply(f => f.Id)}/functions/AKVSQLRotation"),
                        EndpointType = "AzureFunction",
                        MaxEventsPerBatch = 1,
                        PreferredBatchSizeInKilobytes = 64,
                    },
                });

            return true;
        }

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
}
