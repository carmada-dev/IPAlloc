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
        private static readonly IPNetwork DefaultNetwork = IPNetwork.Parse("0.0.0.0/0");

        public static string SerializeIPNetwork(IPNetwork? network)
            => (network ?? DefaultNetwork).ToString().Replace("/", "-");

        public static IPNetwork DeserializeIPNetwork(string? value) 
            => string.IsNullOrEmpty(value) ? DefaultNetwork : IPNetwork.Parse(value.Replace("-", "/"));

        [IgnoreProperty]
        public Guid Key
        {
            get => Guid.TryParse(TableEntity.PartitionKey, out var allocationKey) ? allocationKey : Guid.Empty;
            set => TableEntity.PartitionKey = value.ToString();
        }

        [IgnoreProperty]
        public IPNetwork Network
        { 
            get => DeserializeIPNetwork(TableEntity.RowKey);
            set => TableEntity.RowKey = SerializeIPNetwork(value);
        }

        public string Environment { get; set; }
    }
}
