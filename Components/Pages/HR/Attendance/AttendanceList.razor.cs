using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

namespace COCOBOLOERPNEW.Components.Pages.HR.Attendance;

public partial class AttendanceList : ComponentBase
{
    [Inject] private IAttendanceService AttendanceService { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // Permissions
    private bool _hasViewPermission;
    private bool _hasEditPermission;

    // Loading States
    private bool _isLoadingData = true;
    private bool _isLoadingStats = true;
    private bool _isExporting;

    // Data
    private List<AttendanceListDto> _items = new();
    private AttendanceStatisticsDto _statistics = new();
    private AttendanceFilterDto _filter = new()
    {
        DateFrom = DateTime.Today,
        DateTo = DateTime.Today
    };
    private HashSet<AttendanceListDto> _selectedItems = new();

    // Pagination
    private int _currentPage = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _hasPrevious;
    private bool _hasNext;

    // Edit Dialog
    private bool _showEditDialog;
    private AttendanceListDto? _attendanceToEdit;

    // UI Summary
    private int SummaryPresentCount => _items.Count(x => x.IsPresent);
    private int SummaryAbsentCount => _items.Count(x => x.IsAbsent);
    private int SummaryLateCount => _items.Count(x => x.IsLate);
    private int SummarySpecialCount => _items.Count(x => x.IsHolidayStatus || x.IsOffDayStatus || x.IsPermissionStatus || x.IsErrandStatus || x.HasEarlyLeave);

    private int ActiveFiltersCount =>
        (_filter.DateFrom.HasValue ? 1 : 0) +
        (_filter.DateTo.HasValue ? 1 : 0) +
        (!string.IsNullOrWhiteSpace(_filter.SearchText) ? 1 : 0) +
        (!string.IsNullOrWhiteSpace(_filter.Status) ? 1 : 0) +
        (_filter.LateOnly == true ? 1 : 0) +
        (_filter.AbsentOnly == true ? 1 : 0);

    private int FromRecord => _totalCount == 0 ? 0 : ((_currentPage - 1) * _filter.PageSize) + 1;
    private int ToRecord => Math.Min(_currentPage * _filter.PageSize, _totalCount);

    private string CurrentScopeLabel =>
        _filter.AbsentOnly == true ? "عرض الغياب" :
        _filter.LateOnly == true ? "عرض التأخير" :
        !string.IsNullOrWhiteSpace(_filter.Status) ? $"حالة: {_filter.Status}" :
        "عرض السجلات";

    private string CurrentRangeLabel =>
        _filter.DateFrom.HasValue && _filter.DateTo.HasValue
            ? $"من {_filter.DateFrom.Value:yyyy/MM/dd} إلى {_filter.DateTo.Value:yyyy/MM/dd}"
            : _filter.DateFrom.HasValue
                ? $"من {_filter.DateFrom.Value:yyyy/MM/dd}"
                : _filter.DateTo.HasValue
                    ? $"إلى {_filter.DateTo.Value:yyyy/MM/dd}"
                    : "كل الفترات";

    protected override async Task OnInitializedAsync()
    {
        await LoadPermissions();

        if (_hasViewPermission)
        {
            await LoadData();
            await LoadStatistics();
        }

        _isLoadingData = false;
        _isLoadingStats = false;
    }

    private async Task LoadPermissions()
    {
        var authState = await AuthState.GetAuthenticationStateAsync();
        var user = authState.User;

        _hasViewPermission = user.IsInRole("Admin") || 
                             user.IsInRole("Accountant") ||
                             user.IsInRole("AccountsManager") ||
                             user.IsInRole("HrManager") ||
                             user.IsInRole("HRManager") ||
                             user.HasClaim("Permission", "frm_Attendance:View");
        
        _hasEditPermission = user.IsInRole("Admin") || 
                             user.IsInRole("Accountant") ||
                             user.IsInRole("AccountsManager") ||
                             user.IsInRole("HrManager") ||
                             user.IsInRole("HRManager") ||
                             user.HasClaim("Permission", "frm_Attendance:Edit");
    }

    private async Task LoadData()
    {
        _filter.PageNumber = _currentPage;
        var result = await AttendanceService.GetAttendanceAsync(_filter);

        _items = result.Items;
        _totalCount = result.TotalCount;
        _totalPages = result.TotalPages;
        _hasPrevious = result.HasPrevious;
        _hasNext = result.HasNext;
    }

    private async Task LoadStatistics()
    {
        _statistics = await AttendanceService.GetStatisticsAsync(_filter);
    }

    private async Task ApplyFilter()
    {
        _currentPage = 1;
        _isLoadingData = true;
        _isLoadingStats = true;
        StateHasChanged();

        await LoadData();
        await LoadStatistics();

        _isLoadingData = false;
        _isLoadingStats = false;
        StateHasChanged();
    }

    private async Task ResetFilters()
    {
        _filter = new AttendanceFilterDto
        {
            DateFrom = DateTime.Today,
            DateTo = DateTime.Today,
            PageSize = _filter.PageSize
        };
        await ApplyFilter();
    }

    private async Task OnPageChanged(int page)
    {
        _currentPage = page;
        _isLoadingData = true;
        StateHasChanged();

        await LoadData();

        _isLoadingData = false;
        StateHasChanged();
    }

    private async Task OnPageSizeChanged(int pageSize)
    {
        _filter.PageSize = pageSize;
        _currentPage = 1;
        await ApplyFilter();
    }

    private void OpenEditDialog(AttendanceListDto attendance)
    {
        _attendanceToEdit = attendance;
        _showEditDialog = true;
    }

    private async Task OnAttendanceSaved()
    {
        _showEditDialog = false;
        await ApplyFilter();
    }

    private async Task ExportExcel()
    {
        _isExporting = true;
        StateHasChanged();

        try
        {
            var bytes = await AttendanceService.ExportAttendanceToExcelAsync(_filter);
            var fileName = $"سجل_الحضور_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var base64 = Convert.ToBase64String(bytes);
            
            await JS.InvokeVoidAsync("downloadFileFromBase64", base64, fileName);
            Snackbar.Add("تم التصدير بنجاح", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطأ في التصدير: {ex.Message}", Severity.Error);
        }

        _isExporting = false;
        StateHasChanged();
    }
}