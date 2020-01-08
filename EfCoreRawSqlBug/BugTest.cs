using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EfCoreRawSqlBug
{
    public class BugTest
    {
        public class Person
        {
            public long PersonID { get; set; }
            public string Name { get; set; }
        }

        public class Car
        {
            public long CarID { get; set; }
            public string Name { get; set; }
        }

        public class CarView
        {
            public string Name { get; set; }
        }

        public class PersonView
        {
            public string Name { get; set; }
        }

        public class ParentView : PersonView
        {
            public long ParentID { get; set; }
        }

        /// <summary>
        /// Forced InMemory DbContext
        /// </summary>
        public class TestContext : DbContext
        {
            public TestContext(DbContextOptions<TestContext> options)
            : base(options)
            { }

            public DbSet<Person> People { get; set; }
            public DbSet<Car> Cars { get; set; }

            public DbSet<CarView> CarViews { get; set; }
            public DbSet<ParentView> ParentViews { get; set; }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Person>(entity =>
                {
                    entity.ToTable("People", "test");
                    entity.HasKey(e => e.PersonID);
                });

                modelBuilder.Entity<Car>(entity =>
                {
                    entity.ToTable("Cars", "test");
                    entity.HasKey(e => e.CarID);
                });

                modelBuilder.Entity<CarView>(entity =>
                {
                    entity.HasNoKey();
                });

                modelBuilder.Entity<PersonView>(entity =>
                {
                    entity.HasNoKey();
                });
            }

            public delegate void DisposingHandler(TestContext sender);
            public event DisposingHandler Disposing;
            public override void Dispose()
            {
                base.Dispose();

                Disposing.Invoke(this);
            }
        }

        
        [Fact]
        public async Task BugReportTest()
        {
            using (LocalDBInstanceGenerator contextGenerator = new LocalDBInstanceGenerator())
            {
                using (TestContext dc = contextGenerator.GenerateTestContext())
                {
                    // this will succeed
                    // This generates SQL:
                    // SELECT [Name] FROM [test].[Cars] ORDER BY [Name]
                    Assert.Empty(await dc.CarViews.FromSqlRaw("SELECT [Name] FROM [test].[Cars] ORDER BY [Name]").ToListAsync());

                    // This will fail
                    // This generates SQL:
                    // SELECT [p].[Discriminator], [p].[Name], [p].[ParentID]
                    // FROM(
                    //    SELECT[Name], 11 AS[ParentID] FROM[test].[People] ORDER BY[Name]
                    // ) AS[p]
                    // WHERE[p].[Discriminator] = N'ParentView'
                    Assert.Empty(await dc.ParentViews.FromSqlRaw("SELECT [Name], 11 AS [ParentID] FROM [test].[People] ORDER BY [Name]").ToListAsync());
                }
            }
        }
    }
}
