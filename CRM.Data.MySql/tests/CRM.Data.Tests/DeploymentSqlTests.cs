using System.Text.RegularExpressions;
using CRM.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CRM.Data.Tests;

public sealed class DeploymentSqlTests
{
    private static string ReadDeploymentSql()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "smarterasp", "crear-base-datos.sql");
            if (File.Exists(path)) return File.ReadAllText(path).Replace("\r\n", "\n");
        }
        throw new FileNotFoundException("No se encontro el SQL oficial de instalacion MySQL.");
    }

    [Fact]
    public void DeploymentSqlMatchesAllCurrentEfMigrations()
    {
        using var context = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>()
            .UseMySQL("Server=127.0.0.1;Database=sql_validation;User ID=sql_validation;").Options);
        var expected = context.GetService<IMigrator>().GenerateScript().Replace("\r\n", "\n").Trim();
        var sql = ReadDeploymentSql();
        var firstTable = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);
        Assert.True(firstTable >= 0);
        var normalized = sql[firstTable..].Replace(
            ") ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", ");").Trim();
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void FreshInstallUsesUtf8mb4WithoutDroppingOrSeedingUserData()
    {
        var sql = ReadDeploymentSql();
        Assert.Contains("SET NAMES utf8mb4;", sql);
        var tables = Regex.Matches(sql, @"CREATE TABLE[^\n]*\([\s\S]*?^\)[^;]*;", RegexOptions.Multiline);
        Assert.NotEmpty(tables.Cast<Match>());
        foreach (Match table in tables)
            Assert.EndsWith("ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", table.Value);
        Assert.DoesNotMatch(@"(?im)^\s*(DROP|TRUNCATE|DELETE\s+FROM|CREATE\s+DATABASE)\b", sql);
        Assert.DoesNotMatch(@"(?i)INSERT\s+INTO\s+`?crm_", sql);
    }
}
