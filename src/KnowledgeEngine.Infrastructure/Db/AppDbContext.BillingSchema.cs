using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Db;

public partial class AppDbContext
{
    public async Task EnsureBillingSetupAsync(CancellationToken ct = default)
    {
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS media_jobs (
                    "Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "WorkspaceId" uuid NOT NULL,
                    "BillingJobId" uuid NULL, "Capability" varchar(128) NOT NULL, "Status" varchar(32) NOT NULL,
                    "Route" varchar(32) NOT NULL, "ProviderId" varchar(128) NULL, "ModelId" varchar(256) NULL,
                    "RunnerId" varchar(128) NULL, "ParametersJson" text NOT NULL, "InputAssetIdsJson" text NOT NULL,
                    "OutputAssetIdsJson" text NOT NULL, "EventsJson" text NOT NULL, "CancellationRequested" boolean NOT NULL,
                    "ErrorCode" text NULL, "ErrorMessage" text NULL, "CreatedAt" timestamp with time zone NOT NULL,
                    "StartedAt" timestamp with time zone NULL, "CompletedAt" timestamp with time zone NULL)
                """, ct);
            foreach (var statement in PostgresBillingSchemaStatements)
            {
                await Database.ExecuteSqlRawAsync(statement, ct);
            }
            await Database.ExecuteSqlRawAsync("ALTER TABLE media_jobs ADD COLUMN IF NOT EXISTS \"BillingJobId\" uuid NULL", ct);
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_media_jobs_BillingJobId\" ON media_jobs (\"BillingJobId\")", ct);
            return;
        }

        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS media_jobs (
                "Id" TEXT NOT NULL CONSTRAINT "PK_media_jobs" PRIMARY KEY, "UserId" TEXT NOT NULL,
                "WorkspaceId" TEXT NOT NULL, "BillingJobId" TEXT NULL, "Capability" TEXT NOT NULL,
                "Status" TEXT NOT NULL, "Route" TEXT NOT NULL, "ProviderId" TEXT NULL, "ModelId" TEXT NULL,
                "RunnerId" TEXT NULL, "ParametersJson" TEXT NOT NULL, "InputAssetIdsJson" TEXT NOT NULL,
                "OutputAssetIdsJson" TEXT NOT NULL, "EventsJson" TEXT NOT NULL, "CancellationRequested" INTEGER NOT NULL,
                "ErrorCode" TEXT NULL, "ErrorMessage" TEXT NULL, "CreatedAt" TEXT NOT NULL,
                "StartedAt" TEXT NULL, "CompletedAt" TEXT NULL)
            """, ct);
        await Database.ExecuteSqlRawAsync(SqliteAiJobsCreateStatement, ct);
        await EnsureSqliteAiJobColumnsAsync(ct);
        await EnsureSqliteMediaJobColumnsAsync(ct);
        foreach (var statement in SqliteBillingSchemaStatements)
        {
            await Database.ExecuteSqlRawAsync(statement, ct);
        }
    }

    private async Task EnsureSqliteAiJobColumnsAsync(CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(ai_jobs)";
            if (command.Connection!.State != System.Data.ConnectionState.Open)
            {
                await command.Connection.OpenAsync(ct);
            }
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                columns.Add(reader.GetString(1));
            }
        }

        var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientJobId"] = "ALTER TABLE ai_jobs ADD COLUMN \"ClientJobId\" TEXT NULL",
            ["WorkspaceId"] = "ALTER TABLE ai_jobs ADD COLUMN \"WorkspaceId\" TEXT NULL",
            ["BillingAccountId"] = "ALTER TABLE ai_jobs ADD COLUMN \"BillingAccountId\" TEXT NULL",
            ["PricePlanVersionId"] = "ALTER TABLE ai_jobs ADD COLUMN \"PricePlanVersionId\" TEXT NULL",
            ["DeviceId"] = "ALTER TABLE ai_jobs ADD COLUMN \"DeviceId\" TEXT NULL",
            ["ExecutionMode"] = "ALTER TABLE ai_jobs ADD COLUMN \"ExecutionMode\" TEXT NOT NULL DEFAULT 'LOCAL'",
            ["BillingMode"] = "ALTER TABLE ai_jobs ADD COLUMN \"BillingMode\" TEXT NOT NULL DEFAULT 'LOCAL_FREE'",
            ["DataPolicy"] = "ALTER TABLE ai_jobs ADD COLUMN \"DataPolicy\" TEXT NULL",
            ["ModelPolicy"] = "ALTER TABLE ai_jobs ADD COLUMN \"ModelPolicy\" TEXT NULL",
            ["EstimatedCredits"] = "ALTER TABLE ai_jobs ADD COLUMN \"EstimatedCredits\" TEXT NOT NULL DEFAULT '0'",
            ["ActualCredits"] = "ALTER TABLE ai_jobs ADD COLUMN \"ActualCredits\" TEXT NOT NULL DEFAULT '0'",
            ["EstimatedAmount"] = "ALTER TABLE ai_jobs ADD COLUMN \"EstimatedAmount\" TEXT NOT NULL DEFAULT '0'",
            ["ActualAmount"] = "ALTER TABLE ai_jobs ADD COLUMN \"ActualAmount\" TEXT NOT NULL DEFAULT '0'",
            ["BudgetLimit"] = "ALTER TABLE ai_jobs ADD COLUMN \"BudgetLimit\" TEXT NULL",
            ["Currency"] = "ALTER TABLE ai_jobs ADD COLUMN \"Currency\" TEXT NOT NULL DEFAULT 'CNY'"
        };

        foreach (var addition in additions)
        {
            if (columns.Contains(addition.Key))
            {
                continue;
            }
            await Database.ExecuteSqlRawAsync(addition.Value, ct);
        }
    }

    private async Task EnsureSqliteMediaJobColumnsAsync(CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(media_jobs)";
            if (command.Connection!.State != System.Data.ConnectionState.Open)
            {
                await command.Connection.OpenAsync(ct);
            }
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("BillingJobId"))
        {
            await Database.ExecuteSqlRawAsync("ALTER TABLE media_jobs ADD COLUMN \"BillingJobId\" TEXT NULL", ct);
        }
        await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_media_jobs_BillingJobId\" ON media_jobs (\"BillingJobId\")", ct);
    }

    private static readonly string[] PostgresBillingSchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS ai_jobs (
          id uuid PRIMARY KEY,
          user_id uuid NOT NULL,
          job_type varchar(50) NOT NULL,
          target_type varchar(50) NOT NULL,
          target_id uuid NOT NULL,
          status varchar(50) NOT NULL,
          model varchar(100),
          prompt_version varchar(50),
          input_tokens integer,
          output_tokens integer,
          cost_estimate numeric(20,8),
          error_message varchar(2000),
          retry_count integer NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL,
          started_at timestamptz,
          finished_at timestamptz
        )
        """,
        """
        ALTER TABLE ai_jobs
          ADD COLUMN IF NOT EXISTS client_job_id varchar(160),
          ADD COLUMN IF NOT EXISTS workspace_id uuid,
          ADD COLUMN IF NOT EXISTS billing_account_id uuid,
          ADD COLUMN IF NOT EXISTS price_plan_version_id uuid,
          ADD COLUMN IF NOT EXISTS device_id uuid,
          ADD COLUMN IF NOT EXISTS execution_mode varchar(30) NOT NULL DEFAULT 'LOCAL',
          ADD COLUMN IF NOT EXISTS billing_mode varchar(40) NOT NULL DEFAULT 'LOCAL_FREE',
          ADD COLUMN IF NOT EXISTS data_policy varchar(50),
          ADD COLUMN IF NOT EXISTS model_policy varchar(50),
          ADD COLUMN IF NOT EXISTS estimated_credits numeric(20,6) NOT NULL DEFAULT 0,
          ADD COLUMN IF NOT EXISTS actual_credits numeric(20,6) NOT NULL DEFAULT 0,
          ADD COLUMN IF NOT EXISTS estimated_amount numeric(20,8) NOT NULL DEFAULT 0,
          ADD COLUMN IF NOT EXISTS actual_amount numeric(20,8) NOT NULL DEFAULT 0,
          ADD COLUMN IF NOT EXISTS budget_limit numeric(20,8),
          ADD COLUMN IF NOT EXISTS currency varchar(3) NOT NULL DEFAULT 'CNY'
        """,
        """
        CREATE TABLE IF NOT EXISTS billing_accounts (
          id uuid PRIMARY KEY,
          account_type varchar(30) NOT NULL,
          owner_user_id uuid,
          name varchar(200) NOT NULL,
          currency varchar(3) NOT NULL,
          status varchar(30) NOT NULL,
          version bigint NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS workspace_billing_bindings (
          id uuid PRIMARY KEY,
          workspace_id uuid NOT NULL,
          billing_account_id uuid NOT NULL,
          is_active boolean NOT NULL DEFAULT true,
          effective_from timestamptz NOT NULL,
          effective_to timestamptz,
          created_by_user_id uuid NOT NULL,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS account_entitlements (
          id uuid PRIMARY KEY,
          billing_account_id uuid NOT NULL,
          entitlement_key varchar(120) NOT NULL,
          value_json text NOT NULL,
          effective_from timestamptz NOT NULL,
          effective_to timestamptz,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS price_plan_versions (
          id uuid PRIMARY KEY,
          code varchar(80) NOT NULL,
          version integer NOT NULL,
          currency varchar(3) NOT NULL,
          status varchar(30) NOT NULL,
          effective_from timestamptz NOT NULL,
          effective_to timestamptz,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS price_rules (
          id uuid PRIMARY KEY,
          price_plan_version_id uuid NOT NULL,
          meter_type varchar(50) NOT NULL,
          provider_id varchar(80),
          model_id varchar(160),
          unit varchar(30) NOT NULL,
          unit_size numeric(20,6) NOT NULL,
          credit_rate numeric(20,6) NOT NULL,
          sale_unit_price numeric(20,8) NOT NULL,
          provider_unit_cost numeric(20,8) NOT NULL,
          provider_currency varchar(3) NOT NULL,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS quota_buckets (
          id uuid PRIMARY KEY,
          billing_account_id uuid NOT NULL,
          source varchar(30) NOT NULL,
          granted_credits numeric(20,6) NOT NULL,
          consumed_credits numeric(20,6) NOT NULL DEFAULT 0,
          reserved_credits numeric(20,6) NOT NULL DEFAULT 0,
          effective_from timestamptz NOT NULL,
          expires_at timestamptz,
          priority integer NOT NULL DEFAULT 0,
          version bigint NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS balance_reservations (
          id uuid PRIMARY KEY,
          billing_account_id uuid NOT NULL,
          job_id uuid NOT NULL,
          reserved_credits numeric(20,6) NOT NULL,
          consumed_credits numeric(20,6) NOT NULL DEFAULT 0,
          allocation_json text NOT NULL,
          status varchar(30) NOT NULL,
          idempotency_key varchar(200) NOT NULL,
          expires_at timestamptz NOT NULL,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_tasks (
          id uuid PRIMARY KEY,
          job_id uuid NOT NULL,
          task_type varchar(80) NOT NULL,
          status varchar(30) NOT NULL,
          sequence integer NOT NULL,
          created_at timestamptz NOT NULL,
          started_at timestamptz,
          completed_at timestamptz
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_request_attempts (
          id uuid PRIMARY KEY,
          job_id uuid NOT NULL,
          task_id uuid,
          provider_id varchar(80) NOT NULL,
          requested_model_id varchar(160) NOT NULL,
          actual_model_id varchar(160),
          provider_request_id varchar(200),
          attempt_no integer NOT NULL DEFAULT 1,
          status varchar(40) NOT NULL,
          http_status integer,
          error_code varchar(120),
          is_chargeable boolean NOT NULL DEFAULT true,
          termination_reason varchar(80),
          created_at timestamptz NOT NULL,
          started_at timestamptz,
          completed_at timestamptz
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS usage_events (
          id uuid PRIMARY KEY,
          job_id uuid NOT NULL,
          task_id uuid,
          attempt_id uuid,
          workspace_id uuid,
          billing_account_id uuid,
          provider_id varchar(80) NOT NULL,
          model_id varchar(160) NOT NULL,
          usage_type varchar(50) NOT NULL,
          quantity numeric(20,6) NOT NULL,
          unit varchar(30) NOT NULL,
          usage_source varchar(50) NOT NULL,
          occurred_at timestamptz NOT NULL,
          received_at timestamptz NOT NULL,
          idempotency_key varchar(200) NOT NULL,
          raw_usage_json text,
          reconciliation_status varchar(50) NOT NULL,
          calculated_credits numeric(20,6) NOT NULL,
          calculated_amount numeric(20,8) NOT NULL,
          currency varchar(3) NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS billing_charges (
          id uuid PRIMARY KEY,
          billing_account_id uuid NOT NULL,
          job_id uuid NOT NULL,
          price_plan_version_id uuid NOT NULL,
          charge_type varchar(50) NOT NULL,
          credits numeric(20,6) NOT NULL,
          amount numeric(20,8) NOT NULL,
          currency varchar(3) NOT NULL,
          status varchar(30) NOT NULL,
          idempotency_key varchar(200) NOT NULL,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS provider_costs (
          id uuid PRIMARY KEY,
          job_id uuid NOT NULL,
          attempt_id uuid,
          provider_id varchar(80) NOT NULL,
          model_id varchar(160) NOT NULL,
          provider_amount numeric(20,8) NOT NULL,
          provider_currency varchar(3) NOT NULL,
          exchange_rate_snapshot numeric(20,8) NOT NULL,
          exchange_rate_source varchar(80) NOT NULL,
          exchange_rate_effective_at timestamptz NOT NULL,
          base_currency varchar(3) NOT NULL,
          base_currency_amount numeric(20,8) NOT NULL,
          cost_tags varchar(500),
          idempotency_key varchar(200) NOT NULL,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS account_ledger (
          id uuid PRIMARY KEY,
          billing_account_id uuid NOT NULL,
          business_type varchar(50) NOT NULL,
          business_id uuid NOT NULL,
          action varchar(30) NOT NULL,
          sequence integer NOT NULL,
          credits numeric(20,6) NOT NULL,
          amount numeric(20,8) NOT NULL,
          currency varchar(3) NOT NULL,
          idempotency_key varchar(200) NOT NULL,
          created_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recharge_products (
          id uuid PRIMARY KEY,
          code varchar(80) NOT NULL,
          display_name varchar(160) NOT NULL,
          description varchar(500) NOT NULL,
          currency varchar(3) NOT NULL,
          amount_minor bigint NOT NULL,
          paid_credits numeric(20,6) NOT NULL,
          bonus_credits numeric(20,6) NOT NULL,
          bonus_expires_in_days integer,
          is_active boolean NOT NULL DEFAULT false,
          effective_from timestamptz NOT NULL,
          effective_to timestamptz,
          sort_order integer NOT NULL DEFAULT 0,
          version bigint NOT NULL DEFAULT 0,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recharge_orders (
          id uuid PRIMARY KEY,
          order_no varchar(32) NOT NULL,
          billing_account_id uuid NOT NULL,
          workspace_id uuid NOT NULL,
          initiated_by_user_id uuid NOT NULL,
          recharge_product_id uuid NOT NULL,
          channel varchar(30) NOT NULL,
          channel_scene varchar(30) NOT NULL,
          currency varchar(3) NOT NULL,
          amount_minor bigint NOT NULL,
          paid_credits numeric(20,6) NOT NULL,
          bonus_credits numeric(20,6) NOT NULL,
          bonus_expires_in_days integer,
          pricing_snapshot_json text NOT NULL,
          status varchar(30) NOT NULL,
          provider_trade_no varchar(128),
          idempotency_key varchar(160) NOT NULL,
          expires_at timestamptz NOT NULL,
          paid_at timestamptz,
          fulfilled_at timestamptz,
          closed_at timestamptz,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_attempts (
          id uuid PRIMARY KEY,
          recharge_order_id uuid NOT NULL,
          attempt_no integer NOT NULL,
          channel varchar(30) NOT NULL,
          channel_scene varchar(30) NOT NULL,
          status varchar(30) NOT NULL,
          payload_type varchar(30),
          payment_payload text,
          provider_trade_no varchar(128),
          provider_request_id varchar(160),
          error_code varchar(120),
          error_message varchar(500),
          expires_at timestamptz NOT NULL,
          last_queried_at timestamptz,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_notifications (
          id uuid PRIMARY KEY,
          channel varchar(30) NOT NULL,
          provider_notification_id varchar(160) NOT NULL,
          order_no varchar(32) NOT NULL,
          provider_trade_no varchar(128),
          notification_type varchar(40) NOT NULL,
          signature_valid boolean NOT NULL,
          body_hash varchar(64) NOT NULL,
          status varchar(30) NOT NULL,
          failure_reason varchar(500),
          received_at timestamptz NOT NULL,
          processed_at timestamptz
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_refunds (
          id uuid PRIMARY KEY,
          refund_no varchar(64) NOT NULL,
          recharge_order_id uuid NOT NULL,
          requested_by_user_id uuid NOT NULL,
          reviewed_by_user_id uuid,
          amount_minor bigint NOT NULL,
          paid_credits_to_recover numeric(20,6) NOT NULL,
          bonus_credits_to_recover numeric(20,6) NOT NULL,
          currency varchar(3) NOT NULL,
          status varchar(30) NOT NULL,
          provider_refund_no varchar(128),
          reason_code varchar(120),
          idempotency_key varchar(160) NOT NULL,
          created_at timestamptz NOT NULL,
          updated_at timestamptz NOT NULL,
          completed_at timestamptz
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_jobs_workspace_client ON ai_jobs(workspace_id, client_job_id)",
        "CREATE INDEX IF NOT EXISTS ix_ai_jobs_billing_created ON ai_jobs(billing_account_id, created_at)",
        "CREATE INDEX IF NOT EXISTS ix_billing_accounts_owner ON billing_accounts(owner_user_id, account_type, status)",
        "CREATE INDEX IF NOT EXISTS ix_workspace_billing_binding ON workspace_billing_bindings(workspace_id, is_active)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_workspace_billing_active ON workspace_billing_bindings(workspace_id) WHERE is_active = true",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_price_plan_versions_code ON price_plan_versions(code, version)",
        "CREATE INDEX IF NOT EXISTS ix_price_rules_lookup ON price_rules(price_plan_version_id, meter_type, provider_id, model_id)",
        "CREATE INDEX IF NOT EXISTS ix_quota_buckets_account ON quota_buckets(billing_account_id, expires_at, priority)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_balance_reservation_idempotency ON balance_reservations(idempotency_key)",
        "CREATE INDEX IF NOT EXISTS ix_balance_reservation_job ON balance_reservations(job_id, status)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_usage_event_idempotency ON usage_events(idempotency_key)",
        "CREATE INDEX IF NOT EXISTS ix_usage_event_job ON usage_events(job_id, occurred_at)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_billing_charge_idempotency ON billing_charges(idempotency_key)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_cost_idempotency ON provider_costs(idempotency_key)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_idempotency ON account_ledger(idempotency_key)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_product_code ON recharge_products(code)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_order_no ON recharge_orders(order_no)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_order_idempotency ON recharge_orders(billing_account_id, idempotency_key)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_provider_trade ON recharge_orders(channel, provider_trade_no) WHERE provider_trade_no IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS ix_recharge_order_account_created ON recharge_orders(billing_account_id, created_at)",
        "CREATE INDEX IF NOT EXISTS ix_recharge_order_recovery ON recharge_orders(status, expires_at)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_attempt_order ON payment_attempts(recharge_order_id, attempt_no)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_notification_provider ON payment_notifications(channel, provider_notification_id)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_refund_no ON payment_refunds(refund_no)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_refund_idempotency ON payment_refunds(recharge_order_id, idempotency_key)"
    ];

    private const string SqliteAiJobsCreateStatement =
        """
        CREATE TABLE IF NOT EXISTS ai_jobs (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "UserId" TEXT NOT NULL,
          "ClientJobId" TEXT NULL,
          "WorkspaceId" TEXT NULL,
          "BillingAccountId" TEXT NULL,
          "PricePlanVersionId" TEXT NULL,
          "DeviceId" TEXT NULL,
          "JobType" TEXT NOT NULL,
          "TargetType" TEXT NOT NULL,
          "TargetId" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "ExecutionMode" TEXT NOT NULL DEFAULT 'LOCAL',
          "BillingMode" TEXT NOT NULL DEFAULT 'LOCAL_FREE',
          "Model" TEXT NULL,
          "PromptVersion" TEXT NULL,
          "DataPolicy" TEXT NULL,
          "ModelPolicy" TEXT NULL,
          "InputTokens" INTEGER NULL,
          "OutputTokens" INTEGER NULL,
          "CostEstimate" TEXT NULL,
          "EstimatedCredits" TEXT NOT NULL DEFAULT '0',
          "ActualCredits" TEXT NOT NULL DEFAULT '0',
          "EstimatedAmount" TEXT NOT NULL DEFAULT '0',
          "ActualAmount" TEXT NOT NULL DEFAULT '0',
          "BudgetLimit" TEXT NULL,
          "Currency" TEXT NOT NULL DEFAULT 'CNY',
          "ErrorMessage" TEXT NULL,
          "RetryCount" INTEGER NOT NULL DEFAULT 0,
          "CreatedAt" TEXT NOT NULL,
          "StartedAt" TEXT NULL,
          "FinishedAt" TEXT NULL
        )
        """;

    private static readonly string[] SqliteBillingSchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS billing_accounts (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "AccountType" TEXT NOT NULL,
          "OwnerUserId" TEXT NULL,
          "Name" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "Version" INTEGER NOT NULL DEFAULT 0,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS workspace_billing_bindings (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "WorkspaceId" TEXT NOT NULL,
          "BillingAccountId" TEXT NOT NULL,
          "IsActive" INTEGER NOT NULL DEFAULT 1,
          "EffectiveFrom" TEXT NOT NULL,
          "EffectiveTo" TEXT NULL,
          "CreatedByUserId" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS account_entitlements (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "BillingAccountId" TEXT NOT NULL,
          "EntitlementKey" TEXT NOT NULL,
          "ValueJson" TEXT NOT NULL,
          "EffectiveFrom" TEXT NOT NULL,
          "EffectiveTo" TEXT NULL,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS price_plan_versions (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "Code" TEXT NOT NULL,
          "Version" INTEGER NOT NULL,
          "Currency" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "EffectiveFrom" TEXT NOT NULL,
          "EffectiveTo" TEXT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS price_rules (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "PricePlanVersionId" TEXT NOT NULL,
          "MeterType" TEXT NOT NULL,
          "ProviderId" TEXT NULL,
          "ModelId" TEXT NULL,
          "Unit" TEXT NOT NULL,
          "UnitSize" TEXT NOT NULL,
          "CreditRate" TEXT NOT NULL,
          "SaleUnitPrice" TEXT NOT NULL,
          "ProviderUnitCost" TEXT NOT NULL,
          "ProviderCurrency" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS quota_buckets (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "BillingAccountId" TEXT NOT NULL,
          "Source" TEXT NOT NULL,
          "GrantedCredits" TEXT NOT NULL,
          "ConsumedCredits" TEXT NOT NULL DEFAULT '0',
          "ReservedCredits" TEXT NOT NULL DEFAULT '0',
          "EffectiveFrom" TEXT NOT NULL,
          "ExpiresAt" TEXT NULL,
          "Priority" INTEGER NOT NULL DEFAULT 0,
          "Version" INTEGER NOT NULL DEFAULT 0,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS balance_reservations (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "BillingAccountId" TEXT NOT NULL,
          "JobId" TEXT NOT NULL,
          "ReservedCredits" TEXT NOT NULL,
          "ConsumedCredits" TEXT NOT NULL DEFAULT '0',
          "AllocationJson" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "ExpiresAt" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_tasks (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "JobId" TEXT NOT NULL,
          "TaskType" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "Sequence" INTEGER NOT NULL,
          "CreatedAt" TEXT NOT NULL,
          "StartedAt" TEXT NULL,
          "CompletedAt" TEXT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS ai_request_attempts (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "JobId" TEXT NOT NULL,
          "TaskId" TEXT NULL,
          "ProviderId" TEXT NOT NULL,
          "RequestedModelId" TEXT NOT NULL,
          "ActualModelId" TEXT NULL,
          "ProviderRequestId" TEXT NULL,
          "AttemptNo" INTEGER NOT NULL DEFAULT 1,
          "Status" TEXT NOT NULL,
          "HttpStatus" INTEGER NULL,
          "ErrorCode" TEXT NULL,
          "IsChargeable" INTEGER NOT NULL DEFAULT 1,
          "TerminationReason" TEXT NULL,
          "CreatedAt" TEXT NOT NULL,
          "StartedAt" TEXT NULL,
          "CompletedAt" TEXT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS usage_events (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "JobId" TEXT NOT NULL,
          "TaskId" TEXT NULL,
          "AttemptId" TEXT NULL,
          "WorkspaceId" TEXT NULL,
          "BillingAccountId" TEXT NULL,
          "ProviderId" TEXT NOT NULL,
          "ModelId" TEXT NOT NULL,
          "UsageType" TEXT NOT NULL,
          "Quantity" TEXT NOT NULL,
          "Unit" TEXT NOT NULL,
          "UsageSource" TEXT NOT NULL,
          "OccurredAt" TEXT NOT NULL,
          "ReceivedAt" TEXT NOT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "RawUsageJson" TEXT NULL,
          "ReconciliationStatus" TEXT NOT NULL,
          "CalculatedCredits" TEXT NOT NULL,
          "CalculatedAmount" TEXT NOT NULL,
          "Currency" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS billing_charges (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "BillingAccountId" TEXT NOT NULL,
          "JobId" TEXT NOT NULL,
          "PricePlanVersionId" TEXT NOT NULL,
          "ChargeType" TEXT NOT NULL,
          "Credits" TEXT NOT NULL,
          "Amount" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS provider_costs (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "JobId" TEXT NOT NULL,
          "AttemptId" TEXT NULL,
          "ProviderId" TEXT NOT NULL,
          "ModelId" TEXT NOT NULL,
          "ProviderAmount" TEXT NOT NULL,
          "ProviderCurrency" TEXT NOT NULL,
          "ExchangeRateSnapshot" TEXT NOT NULL,
          "ExchangeRateSource" TEXT NOT NULL,
          "ExchangeRateEffectiveAt" TEXT NOT NULL,
          "BaseCurrency" TEXT NOT NULL,
          "BaseCurrencyAmount" TEXT NOT NULL,
          "CostTags" TEXT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS account_ledger (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "BillingAccountId" TEXT NOT NULL,
          "BusinessType" TEXT NOT NULL,
          "BusinessId" TEXT NOT NULL,
          "Action" TEXT NOT NULL,
          "Sequence" INTEGER NOT NULL,
          "Credits" TEXT NOT NULL,
          "Amount" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recharge_products (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "Code" TEXT NOT NULL,
          "DisplayName" TEXT NOT NULL,
          "Description" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "AmountMinor" INTEGER NOT NULL,
          "PaidCredits" TEXT NOT NULL,
          "BonusCredits" TEXT NOT NULL,
          "BonusExpiresInDays" INTEGER NULL,
          "IsActive" INTEGER NOT NULL DEFAULT 0,
          "EffectiveFrom" TEXT NOT NULL,
          "EffectiveTo" TEXT NULL,
          "SortOrder" INTEGER NOT NULL DEFAULT 0,
          "Version" INTEGER NOT NULL DEFAULT 0,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS recharge_orders (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "OrderNo" TEXT NOT NULL,
          "BillingAccountId" TEXT NOT NULL,
          "WorkspaceId" TEXT NOT NULL,
          "InitiatedByUserId" TEXT NOT NULL,
          "RechargeProductId" TEXT NOT NULL,
          "Channel" TEXT NOT NULL,
          "ChannelScene" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "AmountMinor" INTEGER NOT NULL,
          "PaidCredits" TEXT NOT NULL,
          "BonusCredits" TEXT NOT NULL,
          "BonusExpiresInDays" INTEGER NULL,
          "PricingSnapshotJson" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "ProviderTradeNo" TEXT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "ExpiresAt" TEXT NOT NULL,
          "PaidAt" TEXT NULL,
          "FulfilledAt" TEXT NULL,
          "ClosedAt" TEXT NULL,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_attempts (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "RechargeOrderId" TEXT NOT NULL,
          "AttemptNo" INTEGER NOT NULL,
          "Channel" TEXT NOT NULL,
          "ChannelScene" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "PayloadType" TEXT NULL,
          "PaymentPayload" TEXT NULL,
          "ProviderTradeNo" TEXT NULL,
          "ProviderRequestId" TEXT NULL,
          "ErrorCode" TEXT NULL,
          "ErrorMessage" TEXT NULL,
          "ExpiresAt" TEXT NOT NULL,
          "LastQueriedAt" TEXT NULL,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_notifications (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "Channel" TEXT NOT NULL,
          "ProviderNotificationId" TEXT NOT NULL,
          "OrderNo" TEXT NOT NULL,
          "ProviderTradeNo" TEXT NULL,
          "NotificationType" TEXT NOT NULL,
          "SignatureValid" INTEGER NOT NULL,
          "BodyHash" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "FailureReason" TEXT NULL,
          "ReceivedAt" TEXT NOT NULL,
          "ProcessedAt" TEXT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS payment_refunds (
          "Id" TEXT NOT NULL PRIMARY KEY,
          "RefundNo" TEXT NOT NULL,
          "RechargeOrderId" TEXT NOT NULL,
          "RequestedByUserId" TEXT NOT NULL,
          "ReviewedByUserId" TEXT NULL,
          "AmountMinor" INTEGER NOT NULL,
          "PaidCreditsToRecover" TEXT NOT NULL,
          "BonusCreditsToRecover" TEXT NOT NULL,
          "Currency" TEXT NOT NULL,
          "Status" TEXT NOT NULL,
          "ProviderRefundNo" TEXT NULL,
          "ReasonCode" TEXT NULL,
          "IdempotencyKey" TEXT NOT NULL,
          "CreatedAt" TEXT NOT NULL,
          "UpdatedAt" TEXT NOT NULL,
          "CompletedAt" TEXT NULL
        )
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_jobs_workspace_client ON ai_jobs(\"WorkspaceId\", \"ClientJobId\")",
        "CREATE INDEX IF NOT EXISTS ix_billing_accounts_owner ON billing_accounts(\"OwnerUserId\", \"AccountType\", \"Status\")",
        "CREATE INDEX IF NOT EXISTS ix_workspace_billing_binding ON workspace_billing_bindings(\"WorkspaceId\", \"IsActive\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_workspace_billing_active ON workspace_billing_bindings(\"WorkspaceId\") WHERE \"IsActive\" = 1",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_price_plan_versions_code ON price_plan_versions(\"Code\", \"Version\")",
        "CREATE INDEX IF NOT EXISTS ix_price_rules_lookup ON price_rules(\"PricePlanVersionId\", \"MeterType\", \"ProviderId\", \"ModelId\")",
        "CREATE INDEX IF NOT EXISTS ix_quota_buckets_account ON quota_buckets(\"BillingAccountId\", \"ExpiresAt\", \"Priority\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_balance_reservation_idempotency ON balance_reservations(\"IdempotencyKey\")",
        "CREATE INDEX IF NOT EXISTS ix_balance_reservation_job ON balance_reservations(\"JobId\", \"Status\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_usage_event_idempotency ON usage_events(\"IdempotencyKey\")",
        "CREATE INDEX IF NOT EXISTS ix_usage_event_job ON usage_events(\"JobId\", \"OccurredAt\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_billing_charge_idempotency ON billing_charges(\"IdempotencyKey\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_provider_cost_idempotency ON provider_costs(\"IdempotencyKey\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_idempotency ON account_ledger(\"IdempotencyKey\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_product_code ON recharge_products(\"Code\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_order_no ON recharge_orders(\"OrderNo\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_order_idempotency ON recharge_orders(\"BillingAccountId\", \"IdempotencyKey\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_recharge_provider_trade ON recharge_orders(\"Channel\", \"ProviderTradeNo\") WHERE \"ProviderTradeNo\" IS NOT NULL",
        "CREATE INDEX IF NOT EXISTS ix_recharge_order_account_created ON recharge_orders(\"BillingAccountId\", \"CreatedAt\")",
        "CREATE INDEX IF NOT EXISTS ix_recharge_order_recovery ON recharge_orders(\"Status\", \"ExpiresAt\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_attempt_order ON payment_attempts(\"RechargeOrderId\", \"AttemptNo\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_notification_provider ON payment_notifications(\"Channel\", \"ProviderNotificationId\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_refund_no ON payment_refunds(\"RefundNo\")",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_refund_idempotency ON payment_refunds(\"RechargeOrderId\", \"IdempotencyKey\")"
    ];
}
