using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace IPAlloc.Model
{
    public sealed class AllocationRepository : BaseRepository<AllocationEntity>
    {
        public IAsyncEnumerable<AllocationEntity> GetPartitionAsync(IPNetwork network)
            => GetPartitionAsync(AllocationEntity.SerializeIPNetwork(network) ?? throw new ArgumentNullException(nameof(network)));
    }
}
