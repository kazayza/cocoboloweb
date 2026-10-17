namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>رسالة واحدة في الشاشة (سؤال من المستخدم أو رد من زكي)</summary>
public class ZakiChatItem
{
    public bool IsUser { get; init; }
    public string Text { get; init; } = "";
    public bool Ok { get; init; } = true;
    public int Tokens { get; init; }
    public List<string> Tools { get; init; } = new();
    public List<ZakiTable> Tables { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>رقم الرسالة في قاعدة البيانات (عشان التقييم 👍/👎)</summary>
    public long? MessageId { get; set; }

    /// <summary>التقييم اللي اتسجل: true = مفيد، false = مش مفيد، null = لسه</summary>
    public bool? Useful { get; set; }
}

/// <summary>
/// حالة المحادثة المشتركة لكل جلسة (Scoped):
/// - الصفحة الكاملة والنافذة الصغيرة بيقروا ويكتبوا في نفس الحالة.
/// - بتتحمّل من قاعدة البيانات عند أول فتح (فالمحادثة بتفضل بعد الـ Refresh).
/// - السجل: قائمة المحادثات القديمة، وفتح أي واحدة منها بيكمّل فيها.
/// </summary>
public class ZakiChatState
{
    public List<ZakiChatItem> Items { get; } = new();

    /// <summary>قائمة محادثات المستخدم (الأحدث الأول)</summary>
    public List<ZakiConversationRow> History { get; private set; } = new();

    public long? ConversationId { get; set; }

    public bool IsBusy { get; private set; }

    /// <summary>هل النافذة الصغيرة مفتوحة (على أي شاشة)</summary>
    public bool MiniOpen { get; private set; }

    /// <summary>هل قائمة السجل معروضة دلوقتي</summary>
    public bool HistoryOpen { get; private set; }

    public bool Loaded { get; private set; }

    /// <summary>بيتنادى لما الحالة تتغير، عشان الشاشات تتحدث</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        NotifyChanged();
    }

    public void SetMiniOpen(bool open)
    {
        MiniOpen = open;
        NotifyChanged();
    }

    public void SetHistoryOpen(bool open)
    {
        HistoryOpen = open;
        NotifyChanged();
    }

    /// <summary>محادثة جديدة: نمسح الشاشة ونبدأ محادثة جديدة</summary>
    public void Clear()
    {
        Items.Clear();
        ConversationId = null;
        NotifyChanged();
    }

    /// <summary>بيحمّل آخر محادثة للمستخدم من قاعدة البيانات (مرة واحدة)</summary>
    public async Task EnsureLoadedAsync(ZakiLogService log, string userName, CancellationToken ct = default)
    {
        if (Loaded) return;
        Loaded = true;

        var (conversationId, rows) = await log.LoadLatestConversationAsync(userName, ct);

        if (Items.Count == 0)
            FillItems(rows);

        ConversationId = conversationId;
        NotifyChanged();
    }

    /// <summary>بيحدّث قائمة السجل من قاعدة البيانات</summary>
    public async Task RefreshHistoryAsync(ZakiLogService log, string userName, CancellationToken ct = default)
    {
        History = await log.ListConversationsAsync(userName, 30, ct);
        NotifyChanged();
    }

    /// <summary>يفتح محادثة قديمة من السجل، وأي سؤال جديد بعد كده بيكمّل فيها</summary>
    public async Task OpenConversationAsync(ZakiLogService log, string userName, long conversationId, CancellationToken ct = default)
    {
        var rows = await log.LoadConversationAsync(conversationId, userName, ct);

        Items.Clear();
        FillItems(rows);

        ConversationId = conversationId;
        HistoryOpen = false;
        NotifyChanged();
    }

    /// <summary>بيحوّل صفوف قاعدة البيانات لرسائل على الشاشة (نص بس، من غير الجداول)</summary>
    private void FillItems(List<ZakiHistoryRow> rows)
    {
        foreach (var r in rows)
        {
            Items.Add(new ZakiChatItem { IsUser = true, Text = r.QuestionText ?? "—" });
            Items.Add(new ZakiChatItem
            {
                IsUser = false,
                Text = r.AnswerText ?? "—",
                Ok = r.IsSuccess,
                MessageId = r.MessageId
            });
        }
    }
}
