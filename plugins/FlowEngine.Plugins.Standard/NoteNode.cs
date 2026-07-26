using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 画布注释节点（note），仅用于在画布上添加说明性文本，不参与执行。
/// 该节点为零端口节点（既无输入也无输出），运行时被调度器跳过（ENG2），
/// 校验器也豁免其孤立判定，因此可合法地"断开连接"存在于含连接的工作流中。
/// </summary>
[NodeMeta(TypeName = "note", DisplayName = "Note", Category = NodeCategory.Utility, Icon = "note", DefaultIsEntry = false)]
public sealed class NoteNode : NodeBase
{
    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def("Note", "Utility", false,
            "画布注释节点：仅用于在画布上添加说明性文本，运行时跳过，不占端口、不产生执行记录。",
            ["utility", "note", "annotation"],
            null,
            AiDefinitionHelpers.Example("注释",
                JsonNode.Parse("""{"content":"待办：先清洗数据"}"""),
                JsonNode.Parse("{}")));

    /// <summary>
    /// 注释文本内容，仅用于编辑展示，不参与执行。
    /// </summary>
    [Description("注释文本内容。")]
    [Hint(PresentationHint.TextArea)]
    public string? Content { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// 该节点正常情况下不会被运行时调用（零端口，调度器跳过，ENG2）。
    /// 此处仅作防御性实现，保证即使被调用也安全返回成功。
    /// </remarks>
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            return Task.FromResult(NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject(),
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            }));
        }
        catch (Exception ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, ex.Message);
        }
    }
}
