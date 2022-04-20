using Fengine.Fem.Basis;
using Fengine.Fem.Mesh;
using Fengine.Integration;
using Fengine.LinAlg;
using Fengine.LinAlg.SlaeSolver;
using Fengine.Models;
using Sprache.Calc;

namespace Fengine.Fem;

public class Slae
{
    private readonly IIntegrator _integrator;
    private readonly SlaeSolverGs _slaeSolver;

    public readonly Matrix3Diag Matrix;
    public readonly double[] RhsVec;
    public double[] ResVec;

    public Slae()
    {
        Matrix = null;
        ResVec = null;
        RhsVec = null;
    }

    public Slae(
        IMesh cartesian1DMesh,
        InputFuncs inputFuncs,
        double[] initApprox,
        SlaeSolverGs slaeSolver,
        IIntegrator integrator
    )
    {
        _slaeSolver = slaeSolver;
        _integrator = integrator;

        ResVec = new double[cartesian1DMesh.Nodes.Length];
        initApprox.AsSpan().CopyTo(ResVec);
        RhsVec = new double[cartesian1DMesh.Nodes.Length];

        var localStiffness = BuildLocalStiffness();
        var localMass = BuildLocalMass();

        var upper = new double[cartesian1DMesh.Nodes.Length - 1];
        var center = new double[cartesian1DMesh.Nodes.Length];
        var lower = new double[cartesian1DMesh.Nodes.Length - 1];

        var funcCalc = new XtensibleCalculator();
        var rhsFunc = funcCalc.ParseFunction(inputFuncs.RhsFunc).Compile();
        var lambda = funcCalc.ParseFunction(inputFuncs.Lambda).Compile();
        var gamma = funcCalc.ParseFunction(inputFuncs.Gamma).Compile();

        for (var i = 0; i < cartesian1DMesh.Nodes.Length - 1; i++)
        {
            var step = cartesian1DMesh.Nodes[i + 1].Coordinates["x"] - cartesian1DMesh.Nodes[i].Coordinates["x"];

            #region matrixBuild

            #region center

            center[i] +=
                (lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localStiffness[0][0][0] +
                 lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) * localStiffness[1][0][0]) /
                step +
                (gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localMass[0][0][0] +
                 gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) * localMass[1][0][0]) * step;

            center[i + 1] += (lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) *
                              localStiffness[0][1][1] +
                              lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) *
                              localStiffness[1][1][1]) / step +
                             (gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localMass[0][1][1] +
                              gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) *
                              localMass[1][1][1]) * step;

            #endregion

            #region upper

            upper[i] += (lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localStiffness[0][0][1] +
                         lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) *
                         localStiffness[1][0][1]) / step +
                        (gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localMass[0][0][1] +
                         gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) * localMass[1][0][1]) *
                        step;

            #endregion

            #region lower

            lower[i] += (lambda(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localStiffness[0][1][0] +
                         gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) *
                         localStiffness[1][1][0]) / step +
                        (gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i].Coordinates["x"])) * localMass[0][1][0] +
                         gamma(Utils.MakeDict1D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"])) * localMass[1][1][0]) *
                        step;

            #endregion

            #endregion

            #region buildRhs

            RhsVec[i] += step * (rhsFunc(Utils.MakeDict2D(cartesian1DMesh.Nodes[i].Coordinates["x"], ResVec[i])) *
                                 localMass[2][0][0] +
                                 rhsFunc(Utils.MakeDict2D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"],
                                     ResVec[i + 1])) * localMass[2][0][1]);
            RhsVec[i + 1] += step * (rhsFunc(Utils.MakeDict2D(cartesian1DMesh.Nodes[i].Coordinates["x"], ResVec[i])) *
                                     localMass[2][1][0] +
                                     rhsFunc(Utils.MakeDict2D(cartesian1DMesh.Nodes[i + 1].Coordinates["x"],
                                         ResVec[i + 1])) * localMass[2][1][1]);

            #endregion
        }

        Matrix = new Matrix3Diag(upper, center, lower);
    }

    public Slae(
        Matrix3Diag matrix,
        double[] rhsVec,
        SlaeSolverGs slaeSolver,
        IIntegrator integrator
    )
    {
        Matrix = matrix;
        ResVec = new double[rhsVec.Length];
        RhsVec = rhsVec;
        _slaeSolver = slaeSolver;
        _integrator = integrator;
    }

    public double[] Solve(Accuracy accuracy) => _slaeSolver.Solve(this, accuracy);

    private double[][][] BuildLocalStiffness()
    {
        var mesh = Utils.Create1DIntegrationMesh(0.0, 1.0);

        var localStiffness = new double[2][][];
        localStiffness[0] = new double[2][];
        localStiffness[1] = new double[2][];

        var integralValues = new[]
        {
            _integrator.Integrate1D(mesh, LinearBasis.Func[0]),
            _integrator.Integrate1D(mesh, LinearBasis.Func[1])
        };

        for (var i = 0; i < 2; i++)
        {
            localStiffness[0][i] = new double[2];
            localStiffness[1][i] = new double[2];

            for (var j = 0; j < 2; j++)
            {
                if (i == j)
                {
                    localStiffness[0][i][j] = integralValues[0];
                    localStiffness[1][i][j] = integralValues[1];
                }
                else
                {
                    localStiffness[0][i][j] = -integralValues[0];
                    localStiffness[1][i][j] = -integralValues[1];
                }
            }
        }

        return localStiffness;
    }

    private double[][][] BuildLocalMass()
    {
        var mesh = Utils.Create1DIntegrationMesh(0.0, 1.0);

        var localMass = new double[3][][];
        localMass[0] = new double[2][];
        localMass[1] = new double[2][];
        localMass[2] = new double[2][];

        var integralValues = new[]
        {
            _integrator.Integrate1D(mesh,
                x => LinearBasis.Func[0](x) * LinearBasis.Func[0](x) * LinearBasis.Func[0](x)),
            _integrator.Integrate1D(mesh,
                x => LinearBasis.Func[0](x) * LinearBasis.Func[0](x) * LinearBasis.Func[1](x)),
            _integrator.Integrate1D(mesh,
                x => LinearBasis.Func[0](x) * LinearBasis.Func[1](x) * LinearBasis.Func[1](x)),
            _integrator.Integrate1D(mesh,
                x => LinearBasis.Func[1](x) * LinearBasis.Func[1](x) * LinearBasis.Func[1](x)),
            _integrator.Integrate1D(mesh, x => LinearBasis.Func[0](x) * LinearBasis.Func[0](x)),
            _integrator.Integrate1D(mesh, x => LinearBasis.Func[0](x) * LinearBasis.Func[1](x)),
            _integrator.Integrate1D(mesh, x => LinearBasis.Func[1](x) * LinearBasis.Func[1](x))
        };

        for (var i = 0; i < 2; i++)
        {
            localMass[0][i] = new double[2];
            localMass[1][i] = new double[2];
            localMass[2][i] = new double[2];

            for (var j = 0; j <= i; j++)
            {
                if (i == j)
                {
                    localMass[0][i][j] = integralValues[2 * i];
                    localMass[1][i][j] = integralValues[2 * i + 1];
                    localMass[2][i][j] = integralValues[4 + 2 * i];
                }
                else
                {
                    localMass[0][i][j] = integralValues[i];
                    localMass[0][j][i] = localMass[0][i][j];

                    localMass[1][i][j] = integralValues[2 * i];
                    localMass[1][j][i] = localMass[1][i][j];

                    localMass[2][i][j] = integralValues[4 + i];
                    localMass[2][j][i] = localMass[2][i][j];
                }
            }
        }

        return localMass;
    }
}