using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 节点类型 API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/node-types")]
public class NodeTypesController(
    INodeRegistry nodeRegistry,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    /// <summary>
    /// 获取所有节点类型描述，支持按分类过滤。
    /// 根据 Accept-Language 头自动本地化节点名称、分类、参数名和描述。
    /// </summary>
    /// <param name="category">节点分类过滤条件。</param>
    /// <returns>节点类型描述列表。</returns>
    [HttpGet]
    public ActionResult<IReadOnlyCollection<NodeTypeDescriptor>> GetAll(string? category = null)
    {
        ArgumentNullException.ThrowIfNull(nodeRegistry);
        var descriptors = nodeRegistry.GetDescriptors();

        if (!string.IsNullOrWhiteSpace(category))
        {
            descriptors = descriptors
                .Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var localized = descriptors.Select(LocalizeDescriptor).ToList();
        return Ok(localized);
    }

    private NodeTypeDescriptor LocalizeDescriptor(NodeTypeDescriptor descriptor)
    {
        var displayName = localizer[$"NodeType_{descriptor.TypeName}_DisplayName"];
        var category = localizer[$"Category_{descriptor.Category}"];

        var localizedParams = descriptor.Parameters
            .Select(p => LocalizeParameter(descriptor.TypeName, p))
            .ToList();

        return new NodeTypeDescriptor
        {
            TypeName = descriptor.TypeName,
            DisplayName = displayName.ResourceNotFound ? descriptor.DisplayName : displayName.Value,
            Category = category.ResourceNotFound ? descriptor.Category : category.Value,
            Icon = descriptor.Icon,
            ExecutionMode = descriptor.ExecutionMode,
            Parameters = localizedParams,
            Ports = descriptor.Ports,
            DefaultIsEntry = descriptor.DefaultIsEntry,
        };
    }

    private ParameterDefinition LocalizeParameter(string nodeTypeName, ParameterDefinition param)
    {
        var paramKey = $"{nodeTypeName}_{param.Name}";
        var displayName = localizer[$"Param_{paramKey}_DisplayName"];
        var description = localizer[$"Param_{paramKey}_Description"];

        var localizedOptions = param.Options.Select(opt =>
        {
            var optValueStr = opt.Value?.ToString() ?? string.Empty;
            var optLabel = localizer[$"Option_{paramKey}_{optValueStr}"];
            return new Option
            {
                Label = optLabel.ResourceNotFound ? opt.Label : optLabel.Value,
                Value = opt.Value,
            };
        }).ToList();

        return new ParameterDefinition
        {
            Name = param.Name,
            DisplayName = displayName.ResourceNotFound ? param.DisplayName : displayName.Value,
            Type = param.Type,
            DefaultValue = param.DefaultValue,
            Required = param.Required,
            ValidationRules = param.ValidationRules,
            DisplayRule = param.DisplayRule,
            CredentialType = param.CredentialType,
            Options = localizedOptions,
            Hint = param.Hint,
            HintProperties = param.HintProperties,
            Description = description.ResourceNotFound ? param.Description : description.Value,
            ResourceType = param.ResourceType,
            ItemDefinition = param.ItemDefinition,
            Fields = param.Fields,
        };
    }
}
