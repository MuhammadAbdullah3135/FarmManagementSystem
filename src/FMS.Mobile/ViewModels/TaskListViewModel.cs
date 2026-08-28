using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Models;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class TaskListViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly INotificationService _notifications;
    private readonly ITokenService _tokenService;

    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private string _filterStatus = "All";

    public ObservableCollection<TaskItem> Tasks { get; } = new();

    public ObservableCollection<string> StatusFilters { get; } = new()
    {
        "All", "Pending", "InProgress"
    };

    public TaskListViewModel(IApiService api, IAuthService auth, IDatabaseService db, ISyncService sync, INotificationService notifications, ITokenService tokenService)
    {
        _api = api;
        _auth = auth;
        _db = db;
        _sync = sync;
        _notifications = notifications;
        _tokenService = tokenService;
        Title = "Tasks";
    }

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        _api.SetFarmHeader(farmId.Value);
        await RunBusyAsync(async () =>
        {
            Tasks.Clear();
            OverdueCount = 0;
            try
            {
                var userId = await _tokenService.GetUserIdAsync();
                var endpoint = $"/farm/{farmId}/tasks?pageSize=50";
                if (userId.HasValue)
                    endpoint += $"&assignedEmployeeId={userId}";

                var result = await _api.GetAsync<TaskListResult>(endpoint);
                if (result?.Items is not null)
                {
                    // Apply status filter (client-side)
                    var filtered = result.Items.Where(t =>
                        t.StatusName is "Pending" or "InProgress");

                    if (FilterStatus != "All")
                        filtered = filtered.Where(t => t.StatusName == FilterStatus);

                    foreach (var task in filtered)
                    {
                        task.IsOverdue = task.DueDate < DateTime.Now && task.StatusName != "Completed";
                        Tasks.Add(task);
                    }

                    OverdueCount = Tasks.Count(t => t.IsOverdue);

                    // Fire notification for overdue tasks
                    if (OverdueCount > 0)
                    {
                        var hasPermission = await _notifications.RequestPermissionAsync();
                        if (hasPermission)
                        {
                            _notifications.ShowNotification(
                                $"{OverdueCount} Overdue Task{(OverdueCount > 1 ? "s" : "")}",
                                $"You have {OverdueCount} overdue task{(OverdueCount > 1 ? "s" : "")} that need attention.",
                                notificationId: 9999);
                        }
                    }
                }
            }
            catch
            {
                // Best effort — show cached or empty
            }
        });
    }

    [RelayCommand]
    private async Task StartTaskAsync(TaskItem? task)
    {
        if (task is null) return;
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);
            try
            {
                await _api.PostAsync($"/farm/{farmId}/tasks/{task.Id}/start", new { });
                task.StatusName = "InProgress";
                // Reload to re-apply filter (e.g., "Pending" filter should now hide this task)
                await LoadTasksAsync();
            }
            catch
            {
                await Shell.Current.DisplayAlertAsync("Offline", "Start will sync when online", "OK");
            }
        });
    }

    [RelayCommand]
    private async Task CompleteTaskAsync(TaskItem? task)
    {
        if (task is null) return;
        // Show completion notes prompt
        var notes = await Shell.Current.DisplayPromptAsync(
            "Complete Task",
            "Completion notes (optional):",
            maxLength: 1000,
            keyboard: Keyboard.Text,
            placeholder: "Add notes...");

        // User cancelled the prompt (returns null)
        // We still proceed with completion — notes are optional

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);
            try
            {
                await _api.PostAsync($"/farm/{farmId}/tasks/{task.Id}/complete",
                    new { completionNotes = notes ?? "" });
                Tasks.Remove(task);
                OverdueCount = Tasks.Count(t => t.IsOverdue);
                await Shell.Current.DisplayAlertAsync("Done", "Task completed!", "OK");
            }
            catch
            {
                // Offline: queue the completion
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "TaskComplete",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        taskId = task.Id,
                        completionNotes = notes ?? ""
                    }),
                    CreatedAt = DateTime.UtcNow
                });
                Tasks.Remove(task);
                OverdueCount = Tasks.Count(t => t.IsOverdue);
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlertAsync("Queued", "Completion will sync when online", "OK");
            }
        });
    }

    [RelayCommand]
    private async Task CancelTaskAsync(TaskItem? task)
    {
        if (task is null) return;
        var reason = await Shell.Current.DisplayPromptAsync(
            "Cancel Task",
            "Reason for cancellation (optional):",
            maxLength: 1000,
            keyboard: Keyboard.Text,
            placeholder: "Enter reason...");

        // If user presses Cancel on the prompt, they may have tapped back
        // We should still allow proceeding — reason is optional

        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        await RunBusyAsync(async () =>
        {
            _api.SetFarmHeader(farmId.Value);
            try
            {
                await _api.PostAsync($"/farm/{farmId}/tasks/{task.Id}/cancel",
                    new { cancelReason = reason ?? "" });
                Tasks.Remove(task);
                OverdueCount = Tasks.Count(t => t.IsOverdue);
                await Shell.Current.DisplayAlertAsync("Cancelled", "Task cancelled", "OK");
            }
            catch
            {
                // Offline: queue the cancellation
                await _db.InsertPendingRecordAsync(new PendingRecord
                {
                    EntityType = "TaskCancel",
                    FarmId = farmId.Value,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        taskId = task.Id,
                        cancelReason = reason ?? ""
                    }),
                    CreatedAt = DateTime.UtcNow
                });
                Tasks.Remove(task);
                OverdueCount = Tasks.Count(t => t.IsOverdue);
                var count = await _db.GetPendingCountAsync(farmId.Value);
                _sync.PendingCountChanged?.Invoke(count);
                await Shell.Current.DisplayAlertAsync("Queued", "Cancellation will sync when online", "OK");
            }
        });
    }

    [RelayCommand]
    private async Task FilterByStatusAsync(string? status)
    {
        FilterStatus = status ?? "All";
        await LoadTasksAsync();
    }



    public class TaskListResult
    {
        public List<TaskItem> Items { get; set; } = new();
    }

    public class TaskItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string PriorityName { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public bool IsOverdue { get; set; }
        public string? AssignedEmployeeName { get; set; }
        public string? AnimalName { get; set; }

        // Computed properties for UI
        public string DueDateText => IsOverdue
            ? $"Overdue since {DueDate:MMM dd}"
            : $"Due {DueDate:MMM dd}";

        public Color PriorityColor => PriorityName switch
        {
            "High" => Colors.Red,
            "Medium" => Colors.DodgerBlue,
            _ => Colors.Gray
        };

        public Color StatusColor => StatusName switch
        {
            "Pending" => Colors.Orange,
            "InProgress" => Colors.DodgerBlue,
            "Completed" => Colors.Green,
            _ => Colors.Gray
        };
    }
}
