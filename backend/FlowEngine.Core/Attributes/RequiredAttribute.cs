namespace FlowEngine.Core.Metadata;
/// <summary>标记节点属性为必填参数。供框架/前端进行参数校验与渲染提示。仅作用于属性。
/// 置于独立命名空间以避免与 System.ComponentModel.DataAnnotations.RequiredAttribute 冲突（既有的实体文件已引用后者）。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredAttribute : Attribute
{
}
