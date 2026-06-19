using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 实体基类，提供有序主键生成。
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// 主键。
    /// </summary>
    [Key]
    [Column("id")]
    [Comment("主键")]
    public Guid Id { get; set; }

    /// <summary>
    /// 生成 UUIDv7 格式的有序主键，适用于 SQLite 等需要有序 GUID 的场景。
    /// </summary>
    /// <returns>有序 GUID。</returns>
    public static Guid NewId()
    {
        Span<byte> bytes = stackalloc byte[16];

        // 填充随机字节
        RandomNumberGenerator.Fill(bytes);

        // UUIDv7：高 48 位为 Unix 毫秒时间戳（big-endian）
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // 版本号 7（bits 48-51）
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // 变体 10xx（bits 64-65）
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}