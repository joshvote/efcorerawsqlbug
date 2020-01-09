using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EfCoreRawSqlBug
{
    public class BugTest
    {
        public class Car
        {
            public int CarID { get; set; }
            public string Name { get; set; }
        }

        public class CarView
        {
            public string Name { get; set; }
            public int? Count { get; set; }
            public int? Price { get; set; }
        }

        public class TestContext : DbContext
        {
            public TestContext(DbContextOptions<TestContext> options)
            : base(options)
            { }

            public DbSet<Car> Cars { get; set; }
            public DbSet<CarView> CarViews { get; set; }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Car>(entity =>
                {
                    entity.ToTable("Cars", "test");
                    entity.HasKey(e => e.CarID);
                    entity.Property(e => e.CarID).ValueGeneratedNever();
                });

                modelBuilder.Entity<CarView>(entity =>
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
                    dc.Cars.Add(new Car { CarID = 1, Name = null });
                    await dc.SaveChangesAsync();

                    // Fake query simulating a response where ALL of the columns were NULL
                    List<CarView> allNullFields = await dc.CarViews.FromSqlRaw("SELECT NULL as [Name], NULL AS [Count], NULL AS [Price] FROM [test].[Cars]").ToListAsync();

                    // Fake query simulating a response where SOME but not all of the columns were NULL
                    List<CarView> someNullFields = await dc.CarViews.FromSqlRaw("SELECT NULL as [Name], 123 AS [Count], NULL AS [Price] FROM [test].[Cars]").ToListAsync();

                    Assert.Single(someNullFields);
                    Assert.NotNull(someNullFields.First());
                    Assert.Collection(someNullFields, cv =>
                    {
                        Assert.Null(cv.Name);
                        Assert.Equal(123, cv.Count);
                        Assert.Null(cv.Price);
                    });

                    Assert.Single(allNullFields);
                    Assert.NotNull(allNullFields.First());
                    Assert.Collection(allNullFields, cv =>
                    {
                        Assert.Null(cv.Name);
                        Assert.Null(cv.Count);
                        Assert.Null(cv.Price);
                    });
                }
            }
        }
    }
}
