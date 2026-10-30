namespace Juju.App.Contracts.Bootstrap;

/// <summary>桌面宿主关闭流程；取消令牌可由宿主在关闭超时时发出。</summary>
public interface IAppLifecycle
{
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
