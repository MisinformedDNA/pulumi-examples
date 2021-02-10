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
        var resourceGroupName = config.Get("resourceGroupName") ?? "credrotate2";
        var resourceNamePrefixParam = config.Get("resourceNamePrefix") ?? resourceGroupName;
        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());
        var tenantId = clientConfig.Apply(c => c.TenantId);

        var (vault, resourceGroup) = AddInitalResources(resourceGroupName, resourceNamePrefixParam, tenantId);

        new FunctionStack(vault, resourceGroup);
    }

    private static (Vault, ResourceGroup) AddInitalResources(string resourceGroupName, string? resourceNamePrefix, Output<string> tenantId)
    {
        var location = "CentralUS";
        var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs { ResourceGroupName = resourceGroupName, Location = location });

        resourceNamePrefix ??= resourceGroupName;

        _ = new StorageAccount("appStorageAccount", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        _ = new StorageAccount("appStorageAccount2", new StorageAccountArgs
        {
            AccountName = Output.Format($"{resourceNamePrefix}storage2"),
            Kind = "Storage",
            Location = location,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs { Name = "Standard_LRS" },
        });

        var vaultResource = new Vault("vault", new VaultArgs
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

        return (vaultResource, resourceGroup);
    }
}
