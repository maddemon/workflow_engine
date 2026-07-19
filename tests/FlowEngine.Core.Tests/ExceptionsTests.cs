using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Tests;

public class ExceptionsTests
{
    [Fact]
    public void BusinessException_DefaultConstructor_CreatesInstance()
    {
        var ex = new BusinessException();

        Assert.NotNull(ex);
    }

    [Fact]
    public void BusinessException_MessageConstructor_PreservesMessage()
    {
        var ex = new BusinessException("business error");

        Assert.Equal("business error", ex.Message);
    }

    [Fact]
    public void BusinessException_InnerExceptionConstructor_PreservesValues()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BusinessException("business error", inner);

        Assert.Equal("business error", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void NotFoundException_DefaultConstructor_HasDefaultMessage()
    {
        var ex = new NotFoundException();

        Assert.Equal("资源不存在。", ex.Message);
    }

    [Fact]
    public void NotFoundException_MessageConstructor_PreservesMessage()
    {
        var ex = new NotFoundException("not found");

        Assert.Equal("not found", ex.Message);
    }

    [Fact]
    public void NotFoundException_InnerExceptionConstructor_PreservesValues()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new NotFoundException("not found", inner);

        Assert.Equal("not found", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void PermissionDeniedException_MessageConstructor_PreservesMessage()
    {
        var ex = new PermissionDeniedException("denied");

        Assert.Equal("denied", ex.Message);
    }

    [Fact]
    public void UnauthorizedException_MessageConstructor_PreservesMessage()
    {
        var ex = new UnauthorizedException("unauthorized");

        Assert.Equal("unauthorized", ex.Message);
    }
}
