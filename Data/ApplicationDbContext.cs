using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MygtsMvc.Data;

public class ApplicationDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CurrentBranch
    {
        get
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                if (httpContext.Request.RouteValues.TryGetValue("area", out var areaObj) && areaObj != null)
                {
                    var areaStr = areaObj.ToString();
                    if (string.Equals(areaStr, "RTO", System.StringComparison.OrdinalIgnoreCase)) return "RTO";
                    if (string.Equals(areaStr, "DMG", System.StringComparison.OrdinalIgnoreCase)) return "DMG";
                }

                if (httpContext.Request.Cookies.TryGetValue("mygts_branch", out var branch) &&
                    !string.IsNullOrWhiteSpace(branch))
                {
                    return string.Equals(branch, "RTO", System.StringComparison.OrdinalIgnoreCase) ? "RTO" : "DMG";
                }
            }
            return "DMG";
        }
    }

    public DbSet<UserRecord> Users { get; set; } = null!;
    public DbSet<DeviceRecord> Devices { get; set; } = null!;
    public DbSet<TransactionRecord> Transactions { get; set; } = null!;
    public DbSet<InvoiceRecord> Invoices { get; set; } = null!;
    public DbSet<ConfigItem> Config { get; set; } = null!;
    public DbSet<ManualReminder> ManualReminders { get; set; } = null!;
    public DbSet<RechargeRecord> Recharges { get; set; } = null!;
    public DbSet<VendorRecord> Vendors { get; set; } = null!;
    public DbSet<CompanyRechargeBatch> CompanyRechargeBatches { get; set; } = null!;
    public DbSet<CompanyRechargeItem> CompanyRechargeItems { get; set; } = null!;
    public DbSet<DataShareRecord> DataShareRecords { get; set; } = null!;
    public DbSet<CustomDropdownOption> CustomDropdownOptions { get; set; } = null!;
    public DbSet<VendorCreditRecord> VendorCredits { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<ConfigItem>().HasKey(e => new { e.Key, e.Branch });

        modelBuilder.Entity<DeviceRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<TransactionRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<InvoiceRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<ConfigItem>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<ManualReminder>().HasQueryFilter(e => string.IsNullOrEmpty(e.Branch) || e.Branch == CurrentBranch);
        modelBuilder.Entity<RechargeRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<VendorRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<CompanyRechargeBatch>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<CompanyRechargeItem>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<DataShareRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<CustomDropdownOption>().HasQueryFilter(e => e.Branch == CurrentBranch);
        modelBuilder.Entity<VendorCreditRecord>().HasQueryFilter(e => e.Branch == CurrentBranch);

        modelBuilder.Entity<CompanyRechargeBatch>()
            .HasMany(b => b.Items)
            .WithOne()
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public override int SaveChanges()
    {
        SetBranchOnAddedEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetBranchOnAddedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetBranchOnAddedEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetBranchOnAddedEntities()
    {
        var branch = CurrentBranch;
        var addedEntities = ChangeTracker.Entries().Where(e => e.State == EntityState.Added);
        foreach (var entry in addedEntities)
        {
            if (entry.Entity is IBranchEntity branchEntity)
            {
                if (branchEntity.Branch == "DMG") 
                {
                    branchEntity.Branch = branch;
                }
            }
        }
    }
}
