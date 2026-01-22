using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DenevaManagerTR.Application.Jobs;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Application.Scheduling;

public sealed class MultiJobHostedService : BackgroundService
{
    private readonly ILogger<MultiJobHostedService> _logger;
    private readonly IEnumerable<IJob> _jobs;
    private readonly IDenevaConfigReader _config;

    public MultiJobHostedService(ILogger<MultiJobHostedService> logger, IEnumerable<IJob> jobs, IDenevaConfigReader config)
    {
        _logger = logger;
        _jobs = jobs;
        _config = config;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = _jobs.Select(job => RunJobLoop(job, stoppingToken)).ToArray();
        return Task.WhenAll(tasks);
    }

    private async Task RunJobLoop(IJob job, CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(GetHeartbeatIntervalSeconds());
        using var timer = new PeriodicTimer(interval);

        // Envío inmediato al arrancar
        await SafeExecute(job, token);

        while (!token.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(token);
            await SafeExecute(job, token);
        }
    }

    private int GetHeartbeatIntervalSeconds()
    {
        // deneva.config: Deneva.My.MySettings -> RabbitMQ_SegundosTimerHeartBeat
        const string section = "Deneva.My.MySettings";
        const string key = "RabbitMQ_SegundosTimerHeartBeat";

        var raw = _config.Get(section, key);
        if (!int.TryParse(raw, out var seconds) || seconds <= 0)
            seconds = 30;

        return seconds;
    }

    private async Task SafeExecute(IJob job, CancellationToken token)
    {
        try { await job.ExecuteAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Fallo en job {JobName}.", job.Name); }
    }
}
