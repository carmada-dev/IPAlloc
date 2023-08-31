using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace IPAlloc.Model
{
    public sealed class AllocationEntity : BaseEntity
    {
        public static string? SerializeIPNetwork(IPNetwork? network)
            => network?.ToString().Replace("/", "-");

        public static IPNetwork? DeserializeIPNetwork(string? value) 
            => string.IsNullOrEmpty(value) ? default : IPNetwork.Parse(value.Replace("-", "/"));

        [IgnoreProperty]
        public IPNetwork? NetworkAllocation
        { 
            get => DeserializeIPNetwork(TableEntity.RowKey);
            set => TableEntity.RowKey = SerializeIPNetwork(value);
        }

        [IgnoreProperty]
        public IPNetwork? NetworkPool
        {
            get => DeserializeIPNetwork(TableEntity.PartitionKey);
            set => TableEntity.PartitionKey = SerializeIPNetwork(value);
        }
    }
}
