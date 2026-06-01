using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.DTOs;

#region 📋 List Items

public class AuditLogListItemDto
{
    public long     AuditId         { get; set; }
    public string   TableName       { get; set; } = "";
    public string?  ActionType      { get; set; }
    public string?  PrimaryKeyValue { get; set; }
    public DateTime? ActionDate     { get; set; }
    public string?  LoginName       { get; set; }
    public string?  AppName         { get; set; }
    public string?  HostName        { get; set; }
    public string?  AccessUserName  { get; set; }

    // محسوب للعرض
    public bool HasOldData  { get; set; }
    public bool HasNewData  { get; set; }
}

public class AuditLogDetailDto
{
    public long     AuditId         { get; set; }
    public string   TableName       { get; set; } = "";
    public string?  ActionType      { get; set; }
    public string?  PrimaryKeyValue { get; set; }
    public string?  OldData         { get; set; }
    public string?  NewData         { get; set; }
    public DateTime? ActionDate     { get; set; }
    public string?  LoginName       { get; set; }
    public string?  AppName         { get; set; }
    public string?  HostName        { get; set; }
    public string?  AccessUserName  { get; set; }
}

#endregion

#region 🔍 Filter

public class AuditLogFilterDto
{
    public DateTime? DateFrom     { get; set; }
    public DateTime? DateTo       { get; set; }
    public string?   TableName    { get; set; }
    public string?   ActionType   { get; set; }
    public string?   UserName     { get; set; }
    public string?   SearchText   { get; set; }
    public int       Page         { get; set; } = 1;
    public int       PageSize     { get; set; } = 50;
}

public class PagedAuditLogsDto
{
    public List<AuditLogListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page       { get; set; }
    public int PageSize   { get; set; }
}

#endregion

#region 📊 Dashboard

public class AuditDashboardDto
{
    // Totals
    public int TotalLogs        { get; set; }
    public int TodayLogs        { get; set; }
    public int Last7DaysLogs    { get; set; }
    public int Last30DaysLogs   { get; set; }

    // Action breakdown
    public int InsertCount { get; set; }
    public int UpdateCount { get; set; }
    public int DeleteCount { get; set; }
    public int OtherCount  { get; set; }

    // Top items
    public List<TopActorDto>    TopUsers   { get; set; } = new();
    public List<TopTableDto>    TopTables  { get; set; } = new();
    public List<DailyActivityDto> DailyActivity { get; set; } = new();

    // Recent
    public List<AuditLogListItemDto> RecentLogs { get; set; } = new();
}

public class TopActorDto
{
    public string  UserName { get; set; } = "";
    public int     Count    { get; set; }
    public DateTime? LastActionAt { get; set; }
}

public class TopTableDto
{
    public string TableName { get; set; } = "";
    public int    Count     { get; set; }
}

public class DailyActivityDto
{
    public DateTime Date  { get; set; }
    public string   Label { get; set; } = "";
    public int      Total { get; set; }
    public int      Insert { get; set; }
    public int      Update { get; set; }
    public int      Delete { get; set; }
}

#endregion

#region 🧰 Lookups

public class AuditLookupsDto
{
    public List<string> Tables     { get; set; } = new();
    public List<string> Actions    { get; set; } = new();
    public List<string> UserNames  { get; set; } = new();
}

#endregion
