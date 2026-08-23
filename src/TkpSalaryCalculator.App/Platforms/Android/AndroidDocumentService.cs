using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using TkpSalaryCalculator.App.Presentation.Services;

namespace TkpSalaryCalculator.App;

/// <summary>Storage Access Framework と MAUI FilePicker をストリーム境界へ変換します。</summary>
public sealed class AndroidDocumentService : IPlatformDocumentService
{
    private static int nextRequestCode = 7300;

    public async Task<Stream?> CreateExportAsync(string suggestedFileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        cancellationToken.ThrowIfCancellationRequested();
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("保存先を選択できる画面がありません。");
        var requestCode = Interlocked.Increment(ref nextRequestCode);
        var completion = new TaskCompletionSource<Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnActivityResult(int code, Result result, Intent? data)
        {
            if (code != requestCode) return;
            MainActivity.ActivityResultReceived -= OnActivityResult;
            completion.TrySetResult(result == Result.Ok ? data?.Data : null);
        }

        MainActivity.ActivityResultReceived += OnActivityResult;
        using var registration = cancellationToken.Register(() =>
        {
            MainActivity.ActivityResultReceived -= OnActivityResult;
            completion.TrySetCanceled(cancellationToken);
        });
        try
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/json");
            intent.PutExtra(Intent.ExtraTitle, suggestedFileName);
            activity.StartActivityForResult(intent, requestCode);
            var uri = await completion.Task.ConfigureAwait(false);
            if (uri is null) return null;
            return activity.ContentResolver?.OpenOutputStream(uri, "wt")
                ?? throw new IOException("選択した保存先を開けませんでした。");
        }
        catch
        {
            MainActivity.ActivityResultReceived -= OnActivityResult;
            throw;
        }
    }

    public async Task<Stream?> OpenImportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "インポートするデータを選択",
        });
        cancellationToken.ThrowIfCancellationRequested();
        return result is null ? null : await result.OpenReadAsync().ConfigureAwait(false);
    }
}
