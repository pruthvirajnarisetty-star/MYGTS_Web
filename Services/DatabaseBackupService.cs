using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using System.Globalization;

namespace MygtsMvc.Services;

public class DatabaseBackupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DatabaseBackupService> _logger;

    // Backup interval: 2 hours
    private readonly TimeSpan _backupInterval = TimeSpan.FromHours(2);

    public DatabaseBackupService(
        IServiceProvider serviceProvider, 
        IWebHostEnvironment env, 
        ILogger<DatabaseBackupService> logger)
    {
        _serviceProvider = serviceProvider;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseBackupService starting.");

        try
        {
            // Wait a little bit for the application to fully start
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformBackupAsync(stoppingToken);
                    CleanupAndRotateBackups();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Graceful shutdown requested during backup
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while running automated backup.");
                }

                // Wait 2 hours before the next backup
                await Task.Delay(_backupInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal graceful shutdown on application exit
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DatabaseBackupService.");
        }
        finally
        {
            _logger.LogInformation("DatabaseBackupService stopping.");
        }
    }

    private async Task PerformBackupAsync(CancellationToken stoppingToken)
    {
        var backupDir = Path.Combine(_env.ContentRootPath, "Data", "backups", "Today");
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"mygts_database_{timestamp}.db";
        var backupFilePath = Path.Combine(backupDir, backupFileName);

        _logger.LogInformation("Starting database backup to {BackupFilePath}", backupFilePath);

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            // VACUUM INTO is the safest way to backup a live SQLite database
            var sql = "VACUUM INTO '" + backupFilePath.Replace("'", "''") + "'";
            await dbContext.Database.ExecuteSqlRawAsync(sql, stoppingToken);
        }

        _logger.LogInformation("Database backup completed successfully.");
    }

    private void CleanupAndRotateBackups()
    {
        var rootDir = Path.Combine(_env.ContentRootPath, "Data", "backups");
        if (!Directory.Exists(rootDir)) return;

        // Get all db files in all subdirectories
        var allFiles = Directory.GetFiles(rootDir, "*.db", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        var keepDict = new Dictionary<string, string>(); // FilePath -> TargetDirectoryName
        var now = DateTime.Now.Date;

        // 1. TODAY (Intraday) - Keep ALL saves from today in Today tier
        var todayFiles = allFiles.Where(f => f.CreationTime.Date == now);
        foreach (var file in todayFiles)
        {
            TryKeep(file, "Today");
        }

        // For files created before today, rotate across Daily, Weekly, Monthly, Yearly
        var pastFiles = allFiles.Where(f => f.CreationTime.Date < now).ToList();

        // 2. DAILY - Keep 1 per day for the last 7 days
        var daysLimit = now.AddDays(-7);
        var dailyGroups = pastFiles.Where(f => f.CreationTime >= daysLimit).GroupBy(f => f.CreationTime.ToString("yyyy-MM-dd"));
        foreach (var group in dailyGroups)
        {
            var latest = group.First();
            TryKeep(latest, "Daily");
        }

        // 3. WEEKLY - Keep 1 per week for the last 5 weeks (35 days)
        var weeksLimit = now.AddDays(-35);
        var cal = CultureInfo.CurrentCulture.Calendar;
        var weeklyGroups = pastFiles.Where(f => f.CreationTime >= weeksLimit)
            .GroupBy(f => $"{f.CreationTime.Year}-W{cal.GetWeekOfYear(f.CreationTime, CalendarWeekRule.FirstDay, DayOfWeek.Monday)}");
        foreach (var group in weeklyGroups)
        {
            var latest = group.First();
            TryKeep(latest, "Weekly");
        }

        // 4. MONTHLY - Keep 1 per month for the last 12 months
        var monthsLimit = now.AddMonths(-12);
        var monthlyGroups = pastFiles.Where(f => f.CreationTime >= monthsLimit).GroupBy(f => f.CreationTime.ToString("yyyy-MM"));
        foreach (var group in monthlyGroups)
        {
            var latest = group.First();
            TryKeep(latest, "Monthly");
        }

        // 5. YEARLY (Financial Year: Apr 1 - Mar 31) - Keep 1 per financial year permanently
        var yearlyGroups = pastFiles.GroupBy(f => GetFinancialYear(f.CreationTime));
        foreach (var group in yearlyGroups)
        {
            var latest = group.First();
            TryKeep(latest, "Yearly");
        }

        void TryKeep(FileInfo f, string targetFolder)
        {
            if (!keepDict.ContainsKey(f.FullName))
            {
                keepDict[f.FullName] = targetFolder;
            }
        }

        // Execute moves and deletions
        foreach (var file in allFiles)
        {
            if (keepDict.TryGetValue(file.FullName, out var targetFolder))
            {
                var targetDir = Path.Combine(rootDir, targetFolder);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                
                var newPath = Path.Combine(targetDir, file.Name);
                if (file.FullName != newPath)
                {
                    // Move to new tier folder
                    file.MoveTo(newPath);
                }
            }
            else
            {
                // File is not needed in any tier, delete it
                try
                {
                    file.Delete();
                    _logger.LogInformation("Deleted expired backup file {File}", file.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete expired backup file {File}", file.Name);
                }
            }
        }
    }

    private static int GetFinancialYear(DateTime date)
    {
        return date.Month >= 4 ? date.Year : date.Year - 1;
    }
}
