using WinNotch.Models;

namespace WinNotch.Settings;

public interface IAppSettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    void ApplyStartOnBoot(bool enabled);
}
