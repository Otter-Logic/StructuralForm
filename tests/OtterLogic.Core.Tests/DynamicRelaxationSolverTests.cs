using OtterLogic.Core.FormFinding;
using OtterLogic.Core.FormFinding.Goals;
using OtterLogic.Core.FormFinding.Solvers;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.Core.Tests;

/// <summary>
/// Solver behaviour, exercised on a hanging chain because it needs only
/// Point3d arithmetic — no Rhino instance, so these run in plain CI.
/// </summary>
public class DynamicRelaxationSolverTests
{
    /// <summary>A chain of particles in a straight line, ends pinned, gravity on the rest.</summary>
    private static DynamicRelaxationSolver BuildHangingChain(
        int particles = 21, double span = 10.0, double gravity = 0.05, double slack = 1.3)
    {
        var positions = new Point3d[particles];
        for (int i = 0; i < particles; i++)
            positions[i] = new Point3d(span * i / (particles - 1), 0, 0);

        double segment = span / (particles - 1);
        var goals = new List<IGoal>();

        for (int i = 0; i < particles - 1; i++)
            goals.Add(new SpringGoal(i, i + 1, segment * slack, strength: 1.0));

        goals.Add(new AnchorGoal(0, positions[0]));
        goals.Add(new AnchorGoal(particles - 1, positions[particles - 1]));

        for (int i = 1; i < particles - 1; i++)
            goals.Add(new LoadGoal(i, new Vector3d(0, 0, -gravity)));

        return new DynamicRelaxationSolver(positions, goals);
    }

    [Fact]
    public void Chain_sags_under_gravity()
    {
        var solver = BuildHangingChain();
        solver.Step(3000);

        double lowest = solver.Positions.Min(p => p.Z);
        Assert.True(lowest < -0.1, $"Expected the chain to sag, lowest point was {lowest:0.####}.");
    }

    [Fact]
    public void Chain_is_symmetric_about_midspan()
    {
        var solver = BuildHangingChain();
        solver.Step(3000);

        var positions = solver.Positions;
        int last = positions.Count - 1;

        for (int i = 0; i <= last / 2; i++)
            Assert.Equal(positions[i].Z, positions[last - i].Z, precision: 4);
    }

    [Fact]
    public void Anchors_do_not_move()
    {
        var solver = BuildHangingChain();
        Point3d start = solver.Positions[0];
        Point3d end = solver.Positions[^1];

        solver.Step(3000);

        Assert.True(solver.Positions[0].DistanceTo(start) < 1e-3);
        Assert.True(solver.Positions[^1].DistanceTo(end) < 1e-3);
    }

    [Fact]
    public void Solver_converges_and_then_stops_stepping()
    {
        var solver = BuildHangingChain();
        solver.Step(20_000);

        Assert.True(solver.HasConverged, $"Residual was {solver.Residual:E3}.");

        int settledAt = solver.Iterations;
        solver.Step(100);

        Assert.Equal(settledAt, solver.Iterations);
    }

    [Fact]
    public void Goal_referencing_a_missing_particle_is_rejected()
    {
        var positions = new[] { Point3d.Origin, new Point3d(1, 0, 0) };
        var goals = new IGoal[] { new SpringGoal(0, 7, 1.0) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicRelaxationSolver(positions, goals));
    }

    [Fact]
    public void Spring_pulls_a_stretched_pair_back_to_rest_length()
    {
        var positions = new[] { Point3d.Origin, new Point3d(5, 0, 0) };
        var goals = new IGoal[] { new SpringGoal(0, 1, restLength: 2.0) };

        var solver = new DynamicRelaxationSolver(positions, goals);
        solver.Step(2000);

        double length = solver.Positions[0].DistanceTo(solver.Positions[1]);
        Assert.Equal(2.0, length, precision: 4);
    }
}
