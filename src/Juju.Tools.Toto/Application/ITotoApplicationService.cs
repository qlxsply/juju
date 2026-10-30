using Juju.Tools.Toto.Application.Calendar;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Settings;

namespace Juju.Tools.Toto.Application;

/// <summary>App 可调用的 Toto 用例边界；不暴露 Repository、CSV 行或持久化对象。</summary>
public interface ITotoApplicationService : ITotoItemsApplicationService, ITotoSettingsApplicationService,
    ITotoCalendarApplicationService
{
}
