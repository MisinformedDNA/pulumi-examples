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
        var resourceNamePrefixParam = config.Get("resourceNamePrefixParam");
        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var (resourceGroup, vault) = AddInitalResources(resourceGroupNameParam, resourceNamePrefixParam, tenantId);

        new FunctionStack(vault, resourceGroup);
    }

    private static (ResourceGroup resourceGroup, Vault vault) AddInitalResources(string resourceGroupNameParam, string? resourceNamePrefixParam, Output<string> tenantId)
    {
        var location = "CentralUS";
        var resourceGroupVar = new ResourceGroup("resourceGroup", new ResourceGroupArgs { ResourceGroupName = resourceGroupNameParam, Location = location });

        var resourceNamePrefix = resourceNamePrefixParam is string rnpp ? Output.Create(rnpp) : resourceGroupVar.Name;

        _ = new StorageAccount("storageAccount", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroupNameParam,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        _ = new StorageAccount("storageAccount2", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage2"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroupNameParam,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        var vaultResource = new Vault("vaultResource", new VaultArgs
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
            ResourceGroupName = resourceGroupNameParam,
            VaultName = Output.Format($"{resourceNamePrefix}-kv"),
        });

        return (resourceGroupVar, vaultResource);
    }
}
