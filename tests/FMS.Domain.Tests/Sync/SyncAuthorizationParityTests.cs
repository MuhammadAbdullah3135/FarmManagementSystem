using System.Reflection;
using FMS.API.Controllers;
using FMS.Application.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.Domain.Tests.Sync;

/// <summary>
/// Phase 3.1's discipline, applied to the one new farm-scoped route in phase 5.3.
///
/// <para>
/// A farm-scoped route inherits <c>[Authorize]</c>, which only says "a member of this farm". The
/// per-workflow role lives on the controller of the workflow — and a queued item never passes
/// through that controller, so the sync endpoint has to restate the requirement per operation.
/// These tests are what stop the two from drifting: if one of the three workflows gains a role,
/// the registry entry for its operation must gain it too, or this fails.
/// </para>
/// </summary>
public class SyncAuthorizationParityTests
{
    /// <summary>Which controller owns each queued operation, and the action that live request would hit.</summary>
    private static readonly (string Operation, Type Controller, string Action)[] OperationOwners =
    {
        (SyncOperations.WeightRecord, typeof(AnimalsController), nameof(AnimalsController.AddWeight)),
        (SyncOperations.AttendanceCheckIn, typeof(AttendanceController), nameof(AttendanceController.CheckIn)),
        (SyncOperations.AttendanceCheckOut, typeof(AttendanceController), nameof(AttendanceController.CheckOut)),
        (SyncOperations.TaskComplete, typeof(TasksController), nameof(TasksController.CompleteTask))
    };

    [Fact]
    public void EveryRegisteredOperation_HasAnOwner_AndAllFourAreCovered()
    {
        Assert.Equal(
            SyncOperations.All.OrderBy(o => o),
            OperationOwners.Select(owner => owner.Operation).OrderBy(o => o));
    }

    [Fact]
    public void SyncController_CarriesTheSameAuthorizationAsEveryWorkflowItApplies()
    {
        var sync = typeof(SyncController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(sync);
        Assert.Null(sync!.Roles); // any farm member, matching all three workflows

        foreach (var (_, controller, _) in OperationOwners)
        {
            var target = controller.GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(target);
            Assert.Equal(target!.Roles, sync.Roles);
        }
    }

    [Fact]
    public void EachOperationsRegisteredRoles_MatchTheWorkflowItself()
    {
        foreach (var (operation, controller, action) in OperationOwners)
        {
            var declared = EffectiveRoles(controller, action);
            var registered = SyncOperations.RequiredRoles(operation);

            if (declared is null or { Length: 0 })
            {
                // The workflow is open to any farm member, so the sync gate must be too.
                Assert.Null(registered);
                continue;
            }

            Assert.NotNull(registered);

            foreach (var role in declared.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Assert.Contains(role, registered!);
            }
        }
    }

    [Fact]
    public void SyncRoute_IsFarmScoped_OnTheRouteValueFarmContextMiddlewareEnforces()
    {
        var route = typeof(SyncController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/farm/{farmId:guid}/sync", route!.Template);

        // FarmContextMiddleware resolves the farm from Request.RouteValues["farmId"], so the route
        // value name is the actual link between this endpoint and the enforcement; a differently
        // named parameter would leave the route silently unenforced.
        Assert.Contains("{farmId:guid}", route.Template);
    }

    [Fact]
    public void SyncController_IsAnApiController()
    {
        Assert.NotNull(typeof(SyncController).GetCustomAttribute<ApiControllerAttribute>());
    }

    /// <summary>The role list an action enforces: its own attribute, else its controller's.</summary>
    private static string? EffectiveRoles(Type controller, string action)
    {
        var method = controller.GetMethod(action);

        Assert.NotNull(method);

        var onAction = method!.GetCustomAttribute<AuthorizeAttribute>();
        var onController = controller.GetCustomAttribute<AuthorizeAttribute>();

        return onAction?.Roles ?? onController?.Roles;
    }
}
