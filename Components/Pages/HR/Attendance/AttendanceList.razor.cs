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
                             user.HasClaim("Permission", "frm_Attendance:View");
        
        _hasEditPermission = user.IsInRole("Admin") || 
                             user.IsInRole("Accountant") ||
                             user.IsInRole("AccountsManager") ||
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
            DateTo = DateTime.Today
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