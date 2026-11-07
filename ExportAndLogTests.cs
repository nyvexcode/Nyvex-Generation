using System.Text;
using NyvexGeneration.Core;
using NyvexGeneration.Models;
using NyvexGeneration.Services;
using Xunit;

namespace NyvexGeneration.Tests;

public class ExportServiceTests
{
    private static LookupResult R(string name, LookupStatus status, string details) =>
        new(name, status, LookupFailureReason.None, details, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private readonly ExportService _service = new();

    [Fact]
    public async Task Txt_WritesOneUsernamePerLine()
    {
        using var temp = new TempDirectory();
        var path = temp.File("out.txt");

        await _service.ExportTxtAsync(new[]
        {
            R("aaa", LookupStatus.AccountFound, "x"),
            R("bbb", LookupStatus.NoPublicMatch, "y")
        }, path, CancellationToken.None);

        Assert.Equal(new[] { "aaa", "bbb" }, File.ReadAllLines(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]); // sem BOM
    }

    [Fact]
    public async Task Csv_HasBomHeaderAndRows()
    {
        using var temp = new TempDirectory();
        var path = temp.File("out.csv");

        await _service.ExportCsvAsync(new[] { R("abcd", LookupStatus.AccountFound, "ok") }, path, CancellationToken.None);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal("Username;Comprimento;Status;Detalhes;DataConsulta", lines[0]);
        Assert.StartsWith("abcd;4;Conta encontrada;ok;", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Csv_RespectsDelimiterAndQuotesSpecialCharacters()
    {
        using var temp = new TempDirectory();
        var path = temp.File("out.csv");

        await _service.ExportCsvAsync(
            new[] { R("abcd", LookupStatus.NoPublicMatch, "tem, vírgula e \"aspas\"") },
            path, CancellationToken.None, ',');

        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.StartsWith("Username,Comprimento,Status,Detalhes,DataConsulta", text.TrimStart('﻿'));
        Assert.Contains("\"tem, vírgula e \"\"aspas\"\"\"", text);
    }

    [Theory]
    [InlineData("=1+1", ';', "'=1+1")]
    [InlineData("+cmd", ';', "'+cmd")]
    [InlineData("-x", ';', "'-x")]
    [InlineData("@x", ';', "'@x")]
    [InlineData("normal", ';', "normal")]
    [InlineData("a;b", ';', "\"a;b\"")]
    [InlineData("a;b", ',', "a;b")]
    [InlineData("linha1\nlinha2", ';', "\"linha1\nlinha2\"")]
    [InlineData(null, ';', "")]
    public void EscapeCsv_HandlesInjectionAndQuoting(string? input, char delimiter, string expected)
    {
        Assert.Equal(expected, ExportService.EscapeCsv(input, delimiter));
    }

    [Fact]
    public async Task CreatesMissingDirectories()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a", "b", "out.txt");

        await _service.ExportTxtAsync(new[] { R("aaa", LookupStatus.AccountFound, "x") }, path, CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task EmptyListWritesHeaderOnlyCsvAndEmptyTxt()
    {
        using var temp = new TempDirectory();

        await _service.ExportCsvAsync(Array.Empty<LookupResult>(), temp.File("e.csv"), CancellationToken.None);
        await _service.ExportTxtAsync(Array.Empty<LookupResult>(), temp.File("e.txt"), CancellationToken.None);

        Assert.Single(File.ReadAllLines(temp.File("e.csv")));
        Assert.Equal(string.Empty, File.ReadAllText(temp.File("e.txt")));
    }

    [Fact]
    public async Task InvalidPathAndCancellationAreReported()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ExportTxtAsync(Array.Empty<LookupResult>(), " ", CancellationToken.None));

        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ExportCsvAsync(Array.Empty<LookupResult>(), temp.File("c.csv"), cts.Token));
    }
}

public class LogServiceTests
{
    [Theory]
    [InlineData("cookie=abc123secret", "abc123secret")]
    [InlineData("Authorization: Bearer xyz987token", "xyz987token")]
    [InlineData("x-api-key: KEY-555", "KEY-555")]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData(".ROBLOSECURITY=_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow.|_CAEaAhABsecretvalue", "secretvalue")]
    public void Sanitize_RemovesSecrets(string input, string secret)
    {
        var result = LogService.Sanitize(input);

        Assert.DoesNotContain(secret, result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Sanitize_KeepsHarmlessText()
    {
        Assert.Equal("Lote concluído: 3 encontradas de 50.", LogService.Sanitize("Lote concluído: 3 encontradas de 50."));
        Assert.Null(LogService.Sanitize(null));
    }

    [Fact]
    public void Log_StoresEntryWithAllFieldsAndRaisesEvent()
    {
        var service = new LogService();
        LogEntry? raised = null;
        service.EntryAdded += (_, e) => raised = e;

        service.Log(LogSeverity.Warning, "Roblox", "mensagem", "detalhe técnico");

        var entry = Assert.Single(service.GetRecent(10));
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Equal("Roblox", entry.Category);
        Assert.Equal("mensagem", entry.Message);
        Assert.Equal("detalhe técnico", entry.TechnicalDetails);
        Assert.True(entry.Timestamp > DateTimeOffset.Now.AddMinutes(-1));
        Assert.Same(entry, raised);
    }

    [Fact]
    public void Log_RedactsSecretsInMessageAndDetails()
    {
        var service = new LogService();

        service.Log(LogSeverity.Error, "App", "falha token=SEGREDO1", "cookie: SEGREDO2");

        var entry = service.GetRecent(1)[0];
        Assert.DoesNotContain("SEGREDO1", entry.Message);
        Assert.DoesNotContain("SEGREDO2", entry.TechnicalDetails);
    }

    [Fact]
    public void Log_KeepsOnlyMostRecent2000Entries()
    {
        var service = new LogService();

        for (var i = 0; i < 2105; i++)
        {
            service.Log(LogSeverity.Information, "T", $"m{i}");
        }

        var all = service.GetRecent(10_000);
        Assert.Equal(2000, all.Count);
        Assert.Equal("m2104", all[^1].Message);
        Assert.Equal("m105", all[0].Message);
    }

    [Fact]
    public void Log_WritesDailyFileAndSurvivesClear()
    {
        using var temp = new TempDirectory();
        var service = new LogService(temp.Path);

        service.Log(LogSeverity.Information, "App", "olá", "linha1\nlinha2");
        service.Clear();

        Assert.Empty(service.GetRecent(10));
        var file = Assert.Single(Directory.GetFiles(temp.Path, "nyvex-*.log"));
        var lines = File.ReadAllLines(file);
        Assert.Single(lines);
        Assert.Contains("[Information] [App] olá | linha1 linha2", lines[0]);
    }

    [Fact]
    public void Log_DoesNotThrowWhenDirectoryIsUnusable()
    {
        using var temp = new TempDirectory();
        var blocker = temp.File("arquivo");
        File.WriteAllText(blocker, "x");
        var service = new LogService(Path.Combine(blocker, "sub")); // pai é um arquivo

        var ex = Record.Exception(() => service.Log(LogSeverity.Error, "App", "ainda funciona"));

        Assert.Null(ex);
        Assert.Single(service.GetRecent(10));
    }
}
