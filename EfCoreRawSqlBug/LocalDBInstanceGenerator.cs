using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using static EfCoreRawSqlBug.BugTest;

namespace EfCoreRawSqlBug
{
    /// <summary>
    /// Utility class for creating and managing a SQL Server LocalDB instance. Not suitable for production code.
    /// </summary>
    class LocalDBInstanceGenerator : IDisposable
    {
        protected virtual String DatabaseName => "localdbtest";

        protected String _dbFilePath;
        protected String _dbLogFilePath;
        protected String _dbName;
        protected SqlConnection _dbConnection;
        private bool _contextOpen = false;

        public LocalDBInstanceGenerator()
        {
            SetupDatabase();
        }

        public void Dispose()
        {
            DestroyDatabase();
        }

        private SqlConnection GetConnection(String dbName, bool noDbCatalogue = false)
        {
            if (dbName == null)
            {
                if (noDbCatalogue)
                    return new SqlConnection("server=(localdb)\\MSSQLLocalDB;Integrated Security=true;AttachDBFileName=" + _dbFilePath);
                else
                    return new SqlConnection("server=(localdb)\\MSSQLLocalDB;Initial Catalog=master;Integrated Security=true");
            }
            else
                return new SqlConnection(String.Format("server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Database={0}", dbName));
        }


        /// <summary>
        /// Generates SqliteSalesContext - only a single context may be open at any given time. This is not threadsafe
        /// </summary>
        /// <returns></returns>
        public TestContext GenerateTestContext(bool noDbCatalogue = false)
        {
            if (_contextOpen)
                throw new ArgumentException("There is already a context open");

            _contextOpen = true;

            _dbConnection = GetConnection(noDbCatalogue ? null : _dbName, noDbCatalogue);
            _dbConnection.Open();


            var optionsBuilder = new DbContextOptionsBuilder<TestContext>();
            optionsBuilder.UseSqlServer(_dbConnection);

            var dc = new TestContext(optionsBuilder.Options);
            dc.Disposing += ContextDisposingHandler;
            return dc;
        }

        private void ContextDisposingHandler(TestContext sender)
        {
            _contextOpen = false;
            _dbConnection.Close();
            _dbConnection.Dispose();
            _dbConnection = null;
        }

        /// <summary>
        /// Not even remotely threadsafe, could fail for all sorts of reasons but good enough for a unit test
        /// </summary>
        private void GenerateDbFilePaths()
        {
            string tmpPath = Path.GetTempPath();
            var random = new Random();
            for (int i = 0; i < 10; i++)
            {
                string id = random.Next().ToString("X");
                string fileName = DatabaseName + id + ".mdf";
                string path = Path.Combine(tmpPath, fileName);
                if (!File.Exists(path))
                {
                    _dbFilePath = path;
                    _dbLogFilePath = path.Replace(".mdf", "") + "_log.ldf";
                    _dbName = DatabaseName + id;
                    return;
                }
            }

            throw new Exception("Couldnt create temp file for live database tests. Failed after 10 attempts");
        }

        private string GenerateTmpDirectoryDebugInfo(string header)
        {
            return "\n========= " + header + " ===========\n" +
                "_dbFilePath: " + (_dbFilePath ?? "[NULL]") +
                "\n_dbLogFilePath: " + (_dbLogFilePath ?? "[NULL]") +
                "\nDatabaseName: " + (_dbName ?? "[NULL]") +
                "\n------ FILES --------\n" +
            String.Join("\n", Directory.EnumerateFiles(Path.GetTempPath())) +
            "======================\n";
        }

        /// <summary>
        /// Initialises the DB. Ensure DestroyDatabase is also called
        /// </summary>
        public void SetupDatabase()
        {
            GenerateDbFilePaths();

            try
            {
                //Create the database file
                using (var connection = GetConnection(null))
                {
                    connection.Open();

                    string sql = string.Format(@"
                    IF EXISTS(SELECT * FROM sys.databases WHERE name = '{1}')
                        DROP DATABASE [{1}]

                    CREATE DATABASE
                        [{1}]
                    ON PRIMARY (
                       NAME={1}_data,
                       FILENAME = '{0}'
                    )", _dbFilePath, _dbName
                    );

                    SqlCommand command = new SqlCommand(sql, connection);
                    try
                    {
                        command.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        throw new AggregateException(GenerateTmpDirectoryDebugInfo("Tmp Directory"), ex);
                    }
                }

                //Connect to the database, install tables
                using (var dc = GenerateTestContext(noDbCatalogue: true))
                {
                    dc.Database.EnsureCreated();
                }
            }
            catch (Exception ex)
            {
                DestroyDatabase();
                throw new AggregateException(ex);
            }
        }

        /// <summary>
        /// Destroys the SQLite DB and its file on disk
        /// </summary>
        public void DestroyDatabase()
        {
            try
            {
                if (_dbConnection != null)
                    _dbConnection.Close();
            }
            catch (Exception) { }
            finally
            {
                if (_dbConnection != null)
                {
                    _dbConnection.Dispose();
                    _dbConnection = null;
                }
            }

            using (var connection = GetConnection(null))
            {
                connection.Open();

                //Nuke all connections - then push through our delete
                string sql = string.Format(@"
                    ALTER DATABASE [{0}] SET OFFLINE WITH ROLLBACK IMMEDIATE
                    exec sp_detach_db '{0}'
                    ", _dbName
                );

                SqlCommand command = new SqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }

            if (!String.IsNullOrEmpty(_dbFilePath) && File.Exists(_dbFilePath))
                File.Delete(_dbFilePath);

            if (!String.IsNullOrEmpty(_dbLogFilePath) && File.Exists(_dbLogFilePath))
                File.Delete(_dbLogFilePath);

            _dbFilePath = null;
            _dbLogFilePath = null;
        }
    }
}
