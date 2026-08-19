using System.Threading.Tasks;

namespace DeleuzeMng.Services.Infrastructure
{
    public interface IServiceProvisioningClient
    {
        /// <summary>
        /// サービスを一意に識別するキー ("drive", "chat" など)
        /// </summary>
        string ServiceKey { get; }

        /// <summary>
        /// 対象サービス API にテナント用環境の初期化（スキーマ・テーブル作成）を要求する
        /// </summary>
        Task InitializeTenantAsync(string tenantId);

        /// <summary>
        /// 失敗時の補償処理：対象サービス API にテナント用環境の削除を要求する
        /// </summary>
        Task RollbackTenantAsync(string tenantId);
    }
}