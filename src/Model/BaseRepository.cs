﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace IPAlloc.Model
{
    public abstract class BaseRepository<T> 
        where T : BaseEntity, new()
    {
        private static readonly ConcurrentDictionary<Type, CloudTable> Tables = new ConcurrentDictionary<Type, CloudTable>();

        public string TableName => typeof(T).Name.EndsWith("Entity") ? typeof(T).Name.Substring(0, typeof(T).Name.Length - 6) : typeof(T).Name;

        public CloudTable Table => Tables.GetOrAdd(typeof(T), _ =>
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            var tableClient = storageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(TableName);

            table.CreateIfNotExistsAsync().Wait();
            return table;
        });    

        public virtual async Task<T> InsertAsync(T entity, bool orUpdate = false)
        {
            var operation = orUpdate ? TableOperation.Insert(entity) : TableOperation.InsertOrReplace(entity);
            var result = await Table.ExecuteAsync(operation);
            return (T)result.Result;
        }

        public virtual async Task<T> UpdateAsync(T entity)
        {
            var operation = TableOperation.Replace(entity);
            var result = await Table.ExecuteAsync(operation);
            return (T)result.Result;
        }

        public virtual async Task DeleteAsync(T entity)
        {
            var operation = TableOperation.Delete(entity);
            await Table.ExecuteAsync(operation);
        }

        public virtual async Task<T> GetAsync(string partitionKey, string rowKey)
        {
            var operation = TableOperation.Retrieve<T>(partitionKey, rowKey);
            var result = await Table.ExecuteAsync(operation);
            return (T)result.Result;
        }

        public virtual async IAsyncEnumerable<T> GetPartitionAsync(string partitionKey)
        {
            var query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));
            var token = default(TableContinuationToken);

            do
            {
                var segment = await Table.ExecuteQuerySegmentedAsync(query, token);
            
                foreach (var entity in segment)
                    yield return entity;
                
            } while (token != null);
        }

        public virtual async IAsyncEnumerable<T> GetAllAsync()
        {
            var query = new TableQuery<T>();
            var token = default(TableContinuationToken);

            do 
            {                 
                var segment = await Table.ExecuteQuerySegmentedAsync(query, token);
            
                foreach (var entity in segment)
                    yield return entity;
                
            } while (token != null);
        }

    }
}
