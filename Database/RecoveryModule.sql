-- ============================================================
-- RecoveryModule.sql — ميزة استرداد الفرص الخاسرة (قسم خدمة العملاء)
-- سكربت idempotent: يُنفَّذ أكثر من مرة بأمان (يفحص قبل كل خطوة).
-- يُشغَّل يدويًا على قاعدة البيانات — المشروع بلا Migrations.
-- ⚠️ لو اختلف اسم الجدول عندك عدّله من هذا السكربت.
-- ============================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

PRINT 'RecoveryModule: start';

-- 1) أعمدة الاسترداد على SalesOpportunities
IF COL_LENGTH('dbo.SalesOpportunities', 'IsRecoveryCandidate') IS NULL
    ALTER TABLE dbo.SalesOpportunities ADD IsRecoveryCandidate BIT NOT NULL
        CONSTRAINT DF_SalesOpportunities_IsRecoveryCandidate DEFAULT (0);
PRINT '  IsRecoveryCandidate: ready';

IF COL_LENGTH('dbo.SalesOpportunities', 'RecoveryNotes') IS NULL
    ALTER TABLE dbo.SalesOpportunities ADD RecoveryNotes NVARCHAR(MAX) NULL;
PRINT '  RecoveryNotes: ready';

IF COL_LENGTH('dbo.SalesOpportunities', 'IsRecoveryRejected') IS NULL
    ALTER TABLE dbo.SalesOpportunities ADD IsRecoveryRejected BIT NULL;
PRINT '  IsRecoveryRejected: ready';

-- 2) تسوية التكرارات التاريخية (ناتج سباق التوزيع قبل الفهرس الفريد):
--    يُبقى أحدث مهمة استرداد مفتوحة لكل فرصة ويُغلق الأقدم
;WITH dups AS (
    SELECT t.TaskId,
           ROW_NUMBER() OVER (PARTITION BY t.OpportunityId ORDER BY t.TaskId DESC) AS rn
    FROM dbo.CrmTasks t
    WHERE t.TaskScope = N'Recovery'
      AND t.Status = N'Pending'
      AND t.IsActive = 1
      AND t.OpportunityId IS NOT NULL
)
UPDATE t
   SET t.IsActive       = 0,
       t.Status         = N'Cancelled',
       t.CompletedDate  = GETDATE(),
       t.CompletedBy    = N'RecoverySystem',
       t.CompletionNotes = N'مهمة مكررة (سباق توزيع) — أُغلقت تلقائيًا عند تثبيت الفهرس الفريد'
FROM dbo.CrmTasks t
JOIN dups d ON d.TaskId = t.TaskId
WHERE d.rn > 1;
PRINT '  duplicate open recovery tasks cleaned';

-- 3) ⭐ فهرس فريد مفلتر: مهمة استرداد مفتوحة واحدة كحد أقصى لكل فرصة
--    الحسم النهائي لسباق التوزيع المزدوج — الخدمة تتعامل مع 2601/2627 كـ"أسندت قبلنا"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_CrmTasks_RecoveryOpen_PerOpportunity'
                 AND object_id = OBJECT_ID('dbo.CrmTasks'))
    CREATE UNIQUE INDEX UX_CrmTasks_RecoveryOpen_PerOpportunity
        ON dbo.CrmTasks (OpportunityId)
        WHERE TaskScope = N'Recovery' AND Status = N'Pending' AND IsActive = 1 AND OpportunityId IS NOT NULL;
PRINT '  UX_CrmTasks_RecoveryOpen_PerOpportunity: ready';

PRINT 'RecoveryModule: done';
GO