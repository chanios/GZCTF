﻿using CTFServer.Repositories.Interface;
using CTFServer.Utils;
using System.Threading.Channels;

namespace CTFServer.Services;

public static class ChannelService
{
    internal static IServiceCollection AddChannel<T>(this IServiceCollection services)
    {
        var channel = Channel.CreateUnbounded<T>();
        services.AddSingleton(channel);
        services.AddSingleton(channel.Reader);
        services.AddSingleton(channel.Writer);
        return services;
    }
}

public class FlagChecker : IHostedService
{
    private readonly ILogger<FlagChecker> logger;
    private readonly ChannelReader<Submission> channelReader;
    private readonly IServiceScopeFactory serviceScopeFactory;

    private CancellationTokenSource TokenSource { get; set; } = new CancellationTokenSource();

    public FlagChecker(ChannelReader<Submission> _channelReader,
        ILogger<FlagChecker> _logger,
        IServiceScopeFactory _serviceScopeFactory)
    {
        logger = _logger;
        channelReader = _channelReader;
        serviceScopeFactory = _serviceScopeFactory;
    }

    private async Task Checker(int id, CancellationToken token = default)
    {
        logger.SystemLog($"检查线程 #{id} 已启动", TaskStatus.Pending, LogLevel.Debug);

        await using var scope = serviceScopeFactory.CreateAsyncScope();

        var eventRepository = scope.ServiceProvider.GetRequiredService<IGameEventRepository>();
        var instanceRepository = scope.ServiceProvider.GetRequiredService<IInstanceRepository>();
        var gameNoticeRepository = scope.ServiceProvider.GetRequiredService<IGameNoticeRepository>();
        var gameRepository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var submissionRepository = scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();

        try
        {
            await foreach (var item in channelReader.ReadAllAsync(token))
            {
                logger.SystemLog($"检查线程 #{id} 开始处理提交：{item.Answer}", TaskStatus.Pending, LogLevel.Debug);

                try
                {
                    var instance = await instanceRepository.VerifyAnswer(item, token);

                    if (item.Status == AnswerResult.NotFound)
                        logger.Log($"[实例未知] 未找到队伍 [{item.Team.Name}] 提交题目 [{item.Challenge.Title}] 的实例", item.User!, TaskStatus.NotFound, LogLevel.Warning);
                    else if (item.Status == AnswerResult.Accepted)
                        logger.Log($"[提交正确] 队伍 [{item.Team.Name}] 提交题目 [{item.Challenge.Title}] 的答案 [{item.Answer}]", item.User!, TaskStatus.Success, LogLevel.Information);
                    else
                    {
                        logger.Log($"[提交错误] 队伍 [{item.Team.Name}] 提交题目 [{item.Challenge.Title}] 的答案 [{item.Answer}]", item.User!, TaskStatus.Fail, LogLevel.Information);

                        var result = await instanceRepository.CheckCheat(item, token);

                        if (result.AnswerResult == AnswerResult.CheatDetected)
                        {
                            logger.Log($"[作弊检查] 题目 [{item.Challenge.Title}] 中队伍 [{item.Team.Name}] 疑似作弊，相关队伍 [{result.SourceTeam!.Name}]", item.User!, TaskStatus.Fail, LogLevel.Warning);
                        }
                    }

                    await eventRepository.AddEvent(GameEvent.FromSubmission(item), token);

                    if (item.Status == AnswerResult.Accepted && await instanceRepository.TrySolved(instance!, token))
                    {
                        item.Challenge.AcceptedCount++;
                        NoticeType type = item.Challenge.AcceptedCount switch
                        {
                            1 => NoticeType.FirstBlood,
                            2 => NoticeType.SecondBlood,
                            3 => NoticeType.ThirdBlood,
                            _ => NoticeType.Normal
                        };

                        if (type != NoticeType.Normal)
                            await gameNoticeRepository.CreateNotice(GameNotice.FromSubmission(item, type), token);
                    }

                    gameRepository.FlushScoreboard(item.Game);
                }
                catch (Exception e)
                {
                    logger.SystemLog($"检查线程 #{id} 发生异常", TaskStatus.Fail, LogLevel.Debug);
                    logger.LogError(e.Message, e);
                }

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            logger.SystemLog($"任务取消，检查线程 #{id} 将退出", TaskStatus.Exit, LogLevel.Debug);
        }
        finally
        {
            logger.SystemLog($"检查线程 #{id} 已退出", TaskStatus.Exit, LogLevel.Debug);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TokenSource = new CancellationTokenSource();

        for (int i = 0; i < 4; ++i)
            _ = Checker(i, TokenSource.Token);

        logger.SystemLog("Flag 检查已启用", TaskStatus.Success, LogLevel.Debug);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TokenSource.Cancel();

        logger.SystemLog("Flag 检查已停止", TaskStatus.Exit, LogLevel.Debug);

        return Task.CompletedTask;
    }
}