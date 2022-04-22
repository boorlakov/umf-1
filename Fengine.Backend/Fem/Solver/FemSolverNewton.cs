using Fengine.Backend.DataModels;
using Fengine.Backend.Fem.Mesh;
using Fengine.Backend.Models;

namespace Fengine.Backend.Fem.Solver;

public class FemSolverNewton : IFemSolver
{
    public Statistics Solve(
        IMesh mesh,
        InputFuncs inputFuncs,
        Area area,
        BoundaryConditions boundaryConditions,
        Accuracy accuracy
    )
    {
        throw new NotImplementedException();
    }
}