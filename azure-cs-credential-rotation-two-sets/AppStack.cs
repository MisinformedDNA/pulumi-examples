using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNextGen.Authorization.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest;
using Pulumi.AzureNextGen.KeyVault.Latest.Inputs;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Storage.Latest.Inputs;
using KeyVault = Pulumi.AzureNextGen.KeyVault.Latest;
using Storage = Pulumi.AzureNextGen.Storage.Latest;

class AppStack : Stack
{
    public AppStack()
    {
        var config = new Config();
        var resourceGroupNameParam = config.Require("resourceGroupNameParam");
        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var resourceGroupVar = Output.Create(GetResourceGroup.InvokeAsync(new GetResourceGroupArgs
        {
            ResourceGroupName = resourceGroupNameParam,
        }));
        var resourceNamePrefix = Output.Create(config.Get("resourceNamePrefixParam")) ?? resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Name)!;

        var storageAccount = new StorageAccount("storageAccount", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage"),
            Kind = "Storage",
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            ResourceGroupName = resourceGroupNameParam,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });
        
        var storageAccount2 = new StorageAccount("storageAccount2", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage2"),
            Kind = "Storage",
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
            ResourceGroupName = resourceGroupNameParam,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        var vaultResource = new Vault("vaultResource", new VaultArgs
        {
            Location = resourceGroupVar.Apply(resourceGroupVar => resourceGroupVar.Location),
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
            ResourceGroupName = resourceGroupNameParam,
            VaultName = Output.Format($"{resourceNamePrefix}-kv"),
        });
    }

}
