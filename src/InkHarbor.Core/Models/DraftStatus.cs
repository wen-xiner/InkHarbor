namespace InkHarbor.Core.Models;

/// <summary>
/// 作品状态。与界面设计图的状态标颜色一一对应。
/// </summary>
public enum DraftStatus
{
    /// <summary>底稿（灰）——只生成过、还没动过。</summary>
    Draft,

    /// <summary>修改中（蓝）——已经产生 v2 及以后的版本。</summary>
    Editing,

    /// <summary>已入终稿（绿）——已经复制进 final\。</summary>
    Final
}
