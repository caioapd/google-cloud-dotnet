﻿#region

using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Spanner.Admin.Database.V1;

#endregion

namespace Google.Cloud.Spanner.Data.IntegrationTests
{
    public class TestDatabaseFixture : IDisposable
    {
        // scratch can be used to run tests on a precreated db.
        // all tests are designed to be re-run on an exiting db (previously written state will
        // not cause failures).
        // if you use a scratch database, be sure to comment out the database
        // creation and disposal methods.
        private readonly string _databaseName = //"scratch";  
            "t_" + Guid.NewGuid().ToString("N").Substring(0, 28);
        private readonly string _testTable = "TestTable";
        private Task _creationTask;
        private DatabaseAdminClient _databaseAdminClient;

        public string TestInstanceName => "myspanner";

        public string TestProjectName => "spanneref";

        public string ConnectionString => "Data Source=" + TestProjectName + "/" + TestInstanceName
                                          + "/" + DatabaseName;

        public string DatabaseName => _databaseName;

        public string TestTable => _testTable;

        public void Dispose()
        {
            if (_databaseAdminClient != null)
            {
                _databaseAdminClient.DropDatabase(
                    new DatabaseName(TestProjectName, TestInstanceName, DatabaseName));
            }
        }

        public async Task<SpannerConnection> GetTestDatabaseConnectionAsync()
        {
            await EnsureTestDatabaseAsync();
            return new SpannerConnection(ConnectionString);
        }

        public async Task EnsureTestDatabaseAsync()
        {
            TaskCompletionSource<int> creationTaskCompletionSource;
            if (_creationTask == null)
            {
                creationTaskCompletionSource = new TaskCompletionSource<int>();
                if (Interlocked.CompareExchange(ref _creationTask,
                        creationTaskCompletionSource.Task,
                        null) != null)
                {
                    await _creationTask;
                    return;
                }
            } else
            {
                await _creationTask;
                return;
            }

            await CreateDatabaseAsync();
            await Task.WhenAll(CreateTableAsync(), 
              CreateTypeTableAsync(),
              CreateTxTableAsync()).ConfigureAwait(false);
            await FillSampleData();
            creationTaskCompletionSource.SetResult(0);
        }

        private async Task<Database> CreateDatabaseAsync()
        {
            // Create client
            _databaseAdminClient = await DatabaseAdminClient.CreateAsync();
            // Initialize request argument(s)
            InstanceName parent = new InstanceName(TestProjectName, TestInstanceName);

            string createStatement = "CREATE DATABASE " + DatabaseName;

            // Make the request
            var response = await _databaseAdminClient.CreateDatabaseAsync(parent, createStatement);

            // Poll until the returned long-running operation is complete
            var completedResponse = await response.PollUntilCompletedAsync();

            // Retrieve the operation result
            return completedResponse.Result;
        }

        private async Task CreateTxTableAsync()
        {
            // Create client
            DatabaseAdminClient databaseAdminClient = await DatabaseAdminClient.CreateAsync();

            var typeTable = @"CREATE TABLE TX (
                              K                   STRING(MAX) NOT NULL,
                              Int64Value          INT64,
                              StringValue         STRING(MAX),
                            ) PRIMARY KEY (K)";
            // Make the request
            var response =
                await databaseAdminClient.UpdateDatabaseDdlAsync(new DatabaseName(TestProjectName,
                        TestInstanceName,
                        DatabaseName),
                    new[] {typeTable});

            // Poll until the returned long-running operation is complete
            await response.PollUntilCompletedAsync();
        }

        private async Task CreateTypeTableAsync()
        {
            // Create client
            DatabaseAdminClient databaseAdminClient = await DatabaseAdminClient.CreateAsync();

            var typeTable = @"CREATE TABLE T ( 
                  K                   STRING(MAX) NOT NULL,
                  BoolValue           BOOL,
                  Int64Value          INT64,
                  Float64Value        FLOAT64,
                  StringValue         STRING(MAX),
                  BytesValue          BYTES(MAX),
                  TimestampValue      TIMESTAMP,
                  DateValue           DATE,
                  BoolArrayValue      ARRAY<BOOL>,
                  Int64ArrayValue     ARRAY<INT64>,
                  Float64ArrayValue   ARRAY<FLOAT64>,
                  StringArrayValue    ARRAY<STRING(MAX)>,
                  BytesArrayValue     ARRAY<BYTES(MAX)>,
                  TimestampArrayValue ARRAY<TIMESTAMP>,
                  DateArrayValue      ARRAY<DATE>,
                ) PRIMARY KEY (K)";
            // Make the request
            var response =
                await databaseAdminClient.UpdateDatabaseDdlAsync(new DatabaseName(TestProjectName,
                        TestInstanceName,
                        DatabaseName),
                    new[] {typeTable});

            // Poll until the returned long-running operation is complete
            await response.PollUntilCompletedAsync();
        }

        private async Task CreateTableAsync()
        {
            // Create client
            DatabaseAdminClient databaseAdminClient = await DatabaseAdminClient.CreateAsync();

            string createTableStatement = @"CREATE TABLE " + _testTable + @" (
                                            Key                STRING(MAX) NOT NULL,
                                            StringValue        STRING(MAX),
                                          ) PRIMARY KEY (Key)";
            string index1 = "CREATE INDEX TestTableByValue ON TestTable(StringValue)";
            string index2 = "CREATE INDEX TestTableByValueDesc ON TestTable(StringValue DESC)";

            // Make the request
            var response =
                await databaseAdminClient.UpdateDatabaseDdlAsync(
                    new DatabaseName(TestProjectName, TestInstanceName, DatabaseName),
                    new[] {createTableStatement});

            // Poll until the returned long-running operation is complete
            await response.PollUntilCompletedAsync();

            response =
                await databaseAdminClient.UpdateDatabaseDdlAsync(new DatabaseName(TestProjectName,
                        TestInstanceName,
                        DatabaseName),
                    new[] {index1});
            await response.PollUntilCompletedAsync();

            response =
                await databaseAdminClient.UpdateDatabaseDdlAsync(new DatabaseName(TestProjectName,
                        TestInstanceName,
                        DatabaseName),
                    new[] {index2});
            await response.PollUntilCompletedAsync();
        }

        public async Task FillSampleData()
        {
            using (var connection = new SpannerConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var tx = await connection.BeginTransactionAsync())
                {
                    var cmd = connection.CreateInsertCommand(TestTable,
                        new SpannerParameterCollection
                        {
                            {"Key", SpannerDbType.String},
                            {"StringValue", SpannerDbType.String}
                        });
                    cmd.Transaction = tx;

                    for (var i = 0; i < 15; ++i)
                    {
                        cmd.Parameters["Key"].Value = "k" + i;
                        cmd.Parameters["StringValue"].Value = "v" + i;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                }
            }
        }
    }
}