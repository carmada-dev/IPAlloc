using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace IPAlloc.Model
{
    public abstract class BaseEntity : ITableEntity
    {
        protected ITableEntity TableEntity => this;

        #region ITableEntity implementation

        string ITableEntity.PartitionKey { get; set; }

        string ITableEntity.RowKey { get; set; }

        DateTimeOffset ITableEntity.Timestamp { get; set; }

        string ITableEntity.ETag { get; set; }

        void ITableEntity.ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
        {
            Microsoft.WindowsAzure.Storage.Table.TableEntity.ReadUserObject(this, properties, operationContext);
        }

        IDictionary<string, EntityProperty> ITableEntity.WriteEntity(OperationContext operationContext)
        {
            var properties = Microsoft.WindowsAzure.Storage.Table.TableEntity.WriteUserObject(this, operationContext);

            return properties;
        }

        #endregion ITableEntity implementation
    }
}
