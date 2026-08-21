using Android.App;
using Android.Runtime;

namespace TkpSalaryCalculator.App;

[Application(AllowBackup = false, UsesCleartextTraffic = false)]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
