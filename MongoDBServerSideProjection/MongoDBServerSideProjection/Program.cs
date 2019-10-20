﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Bogus;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using MongoDBServerSideProjection.Extensions;
using MongoDBServerSideProjection.Models;

namespace MongoDBServerSideProjection
{
    class Program
    {
        private const string AccountsCollectionName = "Accounts";
        private const string AccountingDatabaseName = "Accounting";
        private const string FindCommandName = "find";
        private static readonly ObjectId DefaultAccountId = ObjectId.GenerateNewId();
        private static readonly Randomizer Randomizer = new Randomizer();
        
        static async Task Main(string[] args)
        {
            var client = CreateMongoClient();
            
            await SeedAccountsCollection(client);
            
            var accountsCollection = GetAccountsCollection(client);

            Console.WriteLine("No projection");
            // no projection
            var defaultAccountFilterDefinition = Builders<Account>.Filter.Eq(account => account.Id, DefaultAccountId);
            await accountsCollection.Find(defaultAccountFilterDefinition)
                .ToListAsync();

            // projection with build in ObjectProjectionDefinition - requires manual listing of properties
            Console.WriteLine($"{nameof(ObjectProjectionDefinition<Account, AccountSlim>)} projection");
            await accountsCollection.Find(defaultAccountFilterDefinition)
                .Project(new ObjectProjectionDefinition<Account, AccountSlim>(new { id = 1, name = 1}))
                .ToListAsync();
            
            /* This will throw
            await accountsCollection.Find(defaultAccountFilterDefinition)
                .Project(new ObjectProjectionDefinition<Account, AccountSlim>(new AccountSlim()))
                .ToListAsync();
            */
            
            // as projection is client side projection - entire object is returned from mongo
            Console.WriteLine("As projection");
            await accountsCollection.Find(defaultAccountFilterDefinition)
                .Project(Builders<Account>.Projection.As<AccountSlim>())
                .ToListAsync();

            // FindOptions projection - same as above
            await accountsCollection.FindAsync(defaultAccountFilterDefinition, new FindOptions<Account, AccountSlim>());

            // server side projection with custom extension, building projection based on object properties
            await accountsCollection.Find(defaultAccountFilterDefinition)
                .ProjectTo<Account, AccountSlim>()
                .ToListAsync();
        }

        private static MongoClient CreateMongoClient()
        {
            var clientSettings = MongoClientSettings.FromUrl(new MongoUrl("mongodb://localhost:27017"));
            clientSettings.ClusterConfigurator += builder =>
            {
                builder.Subscribe<CommandStartedEvent>(OnCommandStarted);
                builder.Subscribe<CommandSucceededEvent>(OnCommandSucceeded);
            };
            var client = new MongoClient(clientSettings);

            return client;
        }

        private static void OnCommandSucceeded(CommandSucceededEvent @event)
        {
            if (@event.CommandName == FindCommandName)
            {
                Console.WriteLine("Returned document:");
                PrintDocument(@event.Reply);
            }
        }

        private static void OnCommandStarted(CommandStartedEvent @event)
        {
            if (@event.CommandName == FindCommandName)
            {
                Console.WriteLine("Requested document:");
                PrintDocument(@event.Command);
            }
        }

        private static async Task SeedAccountsCollection(MongoClient client)
        {
            var pack = new ConventionPack { new IgnoreExtraElementsConvention(true) };
            ConventionRegistry.Register("Ignore extra elements convention pack", pack, t => true);
            
            var database = client.GetDatabase(AccountingDatabaseName);
            var collectionsCursor = await database.ListCollectionNamesAsync(new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", AccountsCollectionName)
            });
            var hasCollection = await collectionsCursor.AnyAsync();
            
            if (hasCollection == false)
            {
                await database.CreateCollectionAsync(AccountsCollectionName);
            }

            var accountsCollection = database.GetCollection<Account>(AccountsCollectionName);
            accountsCollection.DeleteMany(Builders<Account>.Filter.Empty);
            accountsCollection.InsertOne(new Account
            {
                Id = DefaultAccountId,
                Name = Randomizer.String(10, minChar: 'A', maxChar: 'Z'),
                Transactions = Enumerable.Range(0, 1).Select(_ => new Transaction
                {
                    Id = ObjectId.GenerateNewId(),
                    Amount = Randomizer.Int(1),
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                }).ToList()
            });
        }
        
        private static IMongoCollection<Account> GetAccountsCollection(MongoClient client)
        {
            return client.GetDatabase(AccountingDatabaseName).GetCollection<Account>(AccountsCollectionName);
        }

        private static void PrintDocument(BsonDocument document)
        {
            Console.WriteLine(document.ToJson(new JsonWriterSettings
            {
                Indent = true
            }));
        }
    }
}