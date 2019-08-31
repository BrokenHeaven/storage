﻿#region License
// Copyright (c) 2019 Jake Fowler
//
// Permission is hereby granted, free of charge, to any person 
// obtaining a copy of this software and associated documentation 
// files (the "Software"), to deal in the Software without 
// restriction, including without limitation the rights to use, 
// copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following 
// conditions:
//
// The above copyright notice and this permission notice shall be 
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES 
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR 
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Cmdty.Core.Trees;
using Cmdty.TimePeriodValueTypes;
using Cmdty.TimeSeries;
using JetBrains.Annotations;

namespace Cmdty.Storage
{
    public sealed class TreeStorageValuation<T> : ITreeAddStartingInventory<T>, ITreeAddCurrentPeriod<T>, ITreeAddForwardCurve<T>,
        ITreeAddTreeFactory<T>, ITreeAddCmdtySettlementRule<T>, ITreeAddDiscountFactorFunc<T>, 
            ITreeAddInventoryGridCalculation<T>, ITreeAddInterpolator<T>, ITreeAddNumericalTolerance<T>, ITreeCalculate<T>
        where T : ITimePeriod<T>
    {
        private readonly CmdtyStorage<T> _storage;
        private double _startingInventory;
        private T _currentPeriod;
        private TimeSeries<T, double> _forwardCurve;
        private Func<TimeSeries<T, double>, TimeSeries<T, IReadOnlyList<TreeNode>>> _treeFactory;
        private Func<T, Day> _settleDateRule;
        private Func<Day, double> _discountFactors;
        private Func<CmdtyStorage<T>, IDoubleStateSpaceGridCalc> _gridCalcFactory;
        private IInterpolatorFactory _interpolatorFactory;
        private double _numericalTolerance;

        private TreeStorageValuation([NotNull] CmdtyStorage<T> storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public static ITreeAddStartingInventory<T> ForStorage([NotNull] CmdtyStorage<T> storage)
        {
            return new TreeStorageValuation<T>(storage);
        }

        ITreeAddCurrentPeriod<T> ITreeAddStartingInventory<T>.WithStartingInventory(double inventory)
        {
            if (inventory < 0)
                throw new ArgumentException("Inventory cannot be negative", nameof(inventory));
            _startingInventory = inventory;
            return this;
        }

        ITreeAddForwardCurve<T> ITreeAddCurrentPeriod<T>.ForCurrentPeriod([NotNull] T currentPeriod)
        {
            if (currentPeriod == null)
                throw new ArgumentNullException(nameof(currentPeriod));
            _currentPeriod = currentPeriod;
            return this;
        }

        ITreeAddTreeFactory<T> ITreeAddForwardCurve<T>.WithForwardCurve([NotNull] TimeSeries<T, double> forwardCurve)
        {
            _forwardCurve = forwardCurve ?? throw new ArgumentNullException(nameof(forwardCurve));
            return this;
        }

        ITreeAddCmdtySettlementRule<T> ITreeAddTreeFactory<T>.WithTreeFactory(
            [NotNull] Func<TimeSeries<T, double>, TimeSeries<T, IReadOnlyList<TreeNode>>> treeFactory)
        {
            _treeFactory = treeFactory ?? throw new ArgumentNullException(nameof(treeFactory));
            return this;
        }

        public ITreeAddDiscountFactorFunc<T> WithCmdtySettlementRule([NotNull] Func<T, Day> settleDateRule)
        {
            _settleDateRule = settleDateRule ?? throw new ArgumentNullException(nameof(settleDateRule));
            return this;
        }

        ITreeAddInventoryGridCalculation<T> ITreeAddDiscountFactorFunc<T>.WithDiscountFactorFunc([NotNull] Func<Day, double> discountFactors)
        {
            _discountFactors = discountFactors ?? throw new ArgumentNullException(nameof(discountFactors));
            return this;
        }

        ITreeAddInterpolator<T> ITreeAddInventoryGridCalculation<T>.WithStateSpaceGridCalculation(
            Func<CmdtyStorage<T>, IDoubleStateSpaceGridCalc> gridCalcFactory)
        {
            _gridCalcFactory = gridCalcFactory ?? throw new ArgumentNullException(nameof(gridCalcFactory));
            return this;
        }

        ITreeAddNumericalTolerance<T> ITreeAddInterpolator<T>.WithInterpolatorFactory([NotNull] IInterpolatorFactory interpolatorFactory)
        {
            _interpolatorFactory = interpolatorFactory ?? throw new ArgumentNullException(nameof(interpolatorFactory));
            return this;
        }

        ITreeCalculate<T> ITreeAddNumericalTolerance<T>.WithNumericalTolerance(double numericalTolerance)
        {
            if (numericalTolerance <= 0)
                throw new ArgumentException("Numerical tolerance must be positive.", nameof(numericalTolerance));
            _numericalTolerance = numericalTolerance;
            return this;
        }

        TreeStorageValuationResults<T> ITreeCalculate<T>.Calculate()
        {
            return Calculate(_currentPeriod, _startingInventory, _forwardCurve, _treeFactory, _storage,
                _settleDateRule, _discountFactors, _gridCalcFactory,
                    _interpolatorFactory, _numericalTolerance);
        }

        DecisionSimulator ITreeCalculate<T>.CalculateDecisionSimulator()
        {
            var valuationResults = (this as ITreeCalculate<T>).Calculate();
            return new DecisionSimulator(valuationResults, this);
        }

        double ITreeCalculate<T>.CalculateNpv()
        {
            return (this as ITreeCalculate<T>).Calculate().NetPresentValue;
        }

        private static TreeStorageValuationResults<T> Calculate(T currentPeriod, double startingInventory, 
            TimeSeries<T, double> forwardCurve, Func<TimeSeries<T, double>, TimeSeries<T, IReadOnlyList<TreeNode>>> treeFactory, 
            CmdtyStorage<T> storage, Func<T, Day> settleDateRule, Func<Day, double> discountFactors, 
            Func<CmdtyStorage<T>, IDoubleStateSpaceGridCalc> gridCalcFactory, IInterpolatorFactory interpolatorFactory, 
            double numericalTolerance)
        {
            // TODO think how to avoid repeated code in IntrinsicStorageValuation

            if (startingInventory < 0)
                throw new ArgumentException("Inventory cannot be negative.", nameof(startingInventory));

            if (currentPeriod.CompareTo(storage.EndPeriod) > 0)
                return TreeStorageValuationResults<T>.CreateExpiredResults();

            if (currentPeriod.Equals(storage.EndPeriod))
            {
                if (storage.MustBeEmptyAtEnd)
                {
                    if (startingInventory > 0) // TODO allow some tolerance for floating point numerical error?
                        throw new InventoryConstraintsCannotBeFulfilledException("Storage must be empty at end, but inventory is greater than zero.");
                    return TreeStorageValuationResults<T>.CreateExpiredResults();
                }

                // TODO unit when current period equals end period
                //double terminalMinInventory = storage.MinInventory(storage.EndPeriod);
                //double terminalMaxInventory = storage.MaxInventory(storage.EndPeriod);

                //if (startingInventory < terminalMinInventory)
                //    throw new InventoryConstraintsCannotBeFulfilledException("Current inventory is lower than the minimum allowed in the end period.");

                //if (startingInventory > terminalMaxInventory)
                //    throw new InventoryConstraintsCannotBeFulfilledException("Current inventory is greater than the maximum allowed in the end period.");

                //double cmdtyPrice = forwardCurve[storage.EndPeriod];
                //double npv = storage.TerminalStorageNpv(cmdtyPrice, startingInventory);
                //return new TreeStorageValuationResults<T>(npv);
            }

            TimeSeries<T, InventoryRange> inventorySpace = StorageHelper.CalculateInventorySpace(storage, startingInventory, currentPeriod);

            // TODO think of method to put in TimeSeries class to perform the validation check below in one line
            if (forwardCurve.IsEmpty)
                throw new ArgumentException("Forward curve cannot be empty.", nameof(forwardCurve));

            if (forwardCurve.Start.CompareTo(currentPeriod) > 0)
                throw new ArgumentException("Forward curve starts too late. Must start on or before the current period.", nameof(forwardCurve));

            if (forwardCurve.End.CompareTo(inventorySpace.End) < 0)
                throw new ArgumentException("Forward curve does not extend until storage end period.", nameof(forwardCurve));

            // Perform backward induction
            int numPeriods = inventorySpace.Count + 1; // +1 as inventorySpaceGrid doesn't contain first period
            var storageValueByInventory = new Func<double, double>[numPeriods][];
            var inventorySpaceGrids = new double[numPeriods][];
            var storageNpvs = new double[numPeriods][][];
            var injectWithdrawDecisions = new double[numPeriods][][];

            TimeSeries<T, IReadOnlyList<TreeNode>> spotPriceTree = treeFactory(forwardCurve);

            // Calculate NPVs at end period
            IReadOnlyList<TreeNode> treeNodesForEndPeriod = spotPriceTree[storage.EndPeriod];

            storageValueByInventory[numPeriods - 1] = 
                                    new Func<double, double>[treeNodesForEndPeriod.Count];

            for (int i = 0; i < treeNodesForEndPeriod.Count; i++)
            {
                double cmdtyPrice = treeNodesForEndPeriod[i].Value;
                storageValueByInventory[numPeriods - 1][i] = inventory => storage.TerminalStorageNpv(cmdtyPrice, inventory);
            }

            // Loop back through other periods
            T startActiveStorage = inventorySpace.Start.Offset(-1);
            T[] periodsForResultsTimeSeries = startActiveStorage.EnumerateTo(inventorySpace.End).ToArray();

            int backCounter = numPeriods - 2;
            IDoubleStateSpaceGridCalc gridCalc = gridCalcFactory(storage);

            foreach (T periodLoop in periodsForResultsTimeSeries.Reverse().Skip(1))
            {
                double[] inventorySpaceGrid;
                if (periodLoop.Equals(startActiveStorage))
                {
                    inventorySpaceGrid = new[] {startingInventory};
                }
                else
                {
                    (double inventorySpaceMin, double inventorySpaceMax) = inventorySpace[periodLoop];
                    inventorySpaceGrid = gridCalc.GetGridPoints(inventorySpaceMin, inventorySpaceMax)
                                                    .ToArray();
                }

                (double nextStepInventorySpaceMin, double nextStepInventorySpaceMax) = inventorySpace[periodLoop.Offset(1)];

                Func<double, double>[] continuationValueByInventory = storageValueByInventory[backCounter + 1];

                IReadOnlyList<TreeNode> thisStepTreeNodes = spotPriceTree[periodLoop];
                storageValueByInventory[backCounter] = new Func<double, double>[thisStepTreeNodes.Count];
                var storageNpvsByPriceLevelAndInventory = new double[thisStepTreeNodes.Count][];
                var decisionVolumesByPriceLevelAndInventory = new double[thisStepTreeNodes.Count][];

                for (var priceLevelIndex = 0; priceLevelIndex < thisStepTreeNodes.Count; priceLevelIndex++)
                {
                    TreeNode treeNode = thisStepTreeNodes[priceLevelIndex];
                    var storageValuesGrid = new double[inventorySpaceGrid.Length];
                    var decisionVolumesGrid = new double[inventorySpaceGrid.Length];
                    
                    for (int i = 0; i < inventorySpaceGrid.Length; i++)
                    {
                        double inventory = inventorySpaceGrid[i];
                        (storageValuesGrid[i], decisionVolumesGrid[i], _, _) = 
                                        OptimalDecisionAndValue(storage, periodLoop, inventory,
                                        nextStepInventorySpaceMin, nextStepInventorySpaceMax, treeNode,
                                        continuationValueByInventory, settleDateRule, discountFactors, numericalTolerance);
                    }

                    storageValueByInventory[backCounter][priceLevelIndex] =
                        interpolatorFactory.CreateInterpolator(inventorySpaceGrid, storageValuesGrid);
                    storageNpvsByPriceLevelAndInventory[priceLevelIndex] = storageValuesGrid;
                    decisionVolumesByPriceLevelAndInventory[priceLevelIndex] = decisionVolumesGrid;
                }
                inventorySpaceGrids[backCounter] = inventorySpaceGrid;
                storageNpvs[backCounter] = storageNpvsByPriceLevelAndInventory;
                injectWithdrawDecisions[backCounter] = decisionVolumesByPriceLevelAndInventory;
                backCounter--;
            }

            // Calculate NPVs for first active period using current inventory
            double storageNpv = 0;
            IReadOnlyList<TreeNode> startTreeNodes = spotPriceTree[startActiveStorage];

            for (var i = 0; i < startTreeNodes.Count; i++)
            {
                TreeNode treeNode = startTreeNodes[i];
                storageNpv += storageNpvs[0][i][0] * treeNode.Probability;
            }

            var storageNpvByInventory =
                new TimeSeries<T, IReadOnlyList<Func<double, double>>>(periodsForResultsTimeSeries, storageValueByInventory);
            var inventorySpaceGridsTimeSeries =
                new TimeSeries<T, IReadOnlyList<double>>(periodsForResultsTimeSeries, inventorySpaceGrids);
            var storageNpvsTimeSeries =
                new TimeSeries<T, IReadOnlyList<IReadOnlyList<double>>>(periodsForResultsTimeSeries, storageNpvs);
            var injectWithdrawDecisionsTimeSeries =
                new TimeSeries<T, IReadOnlyList<IReadOnlyList<double>>>(periodsForResultsTimeSeries, injectWithdrawDecisions);

            return new TreeStorageValuationResults<T>(storageNpv, spotPriceTree, storageNpvByInventory, 
                            inventorySpaceGridsTimeSeries, storageNpvsTimeSeries, injectWithdrawDecisionsTimeSeries,
                            inventorySpace);
        }

        // TODO create class on hold this tuple?
        private static (double StorageNpv, double OptimalInjectWithdraw, double CmdtyConsumedOnAction, double ImmediateNpv) 
            OptimalDecisionAndValue(CmdtyStorage<T> storage, T periodLoop, double inventory,
                    double nextStepInventorySpaceMin, double nextStepInventorySpaceMax, TreeNode treeNode,
                    IReadOnlyList<Func<double, double>> continuationValueByInventories, Func<T, Day> settleDateRule, 
                    Func<Day, double> discountFactors, double numericalTolerance)
        {
            InjectWithdrawRange injectWithdrawRange = storage.GetInjectWithdrawRange(periodLoop, inventory);
            double[] decisionSet = StorageHelper.CalculateBangBangDecisionSet(injectWithdrawRange, inventory,
                                            nextStepInventorySpaceMin, nextStepInventorySpaceMax, numericalTolerance);

            var valuesForDecisions = new double[decisionSet.Length];
            var cmdtyConsumedForDecisions = new double[decisionSet.Length];
            var immediateNpvs = new double[decisionSet.Length];
            for (var j = 0; j < decisionSet.Length; j++)
            {
                double decisionInjectWithdraw = decisionSet[j];
                (double immediateNpv, double cmdtyConsumed) = StorageHelper.StorageImmediateNpvForDecision(storage, periodLoop, inventory,
                                                decisionInjectWithdraw, treeNode.Value, settleDateRule, discountFactors);
                // Expected continuation value
                double inventoryAfterDecision = inventory + decisionInjectWithdraw;
                double expectedContinuationValue = 0.0;

                foreach (NodeTransition transition in treeNode.Transitions)
                {
                    int indexOfNextNode = transition.DestinationNode.ValueLevelIndex;
                    double continuationValue = continuationValueByInventories[indexOfNextNode](inventoryAfterDecision);
                    expectedContinuationValue += continuationValue * transition.Probability;
                }

                valuesForDecisions[j] = immediateNpv + expectedContinuationValue;
                cmdtyConsumedForDecisions[j] = cmdtyConsumed;
                immediateNpvs[j] = immediateNpv;
            }

            (double storageNpv, int indexOfOptimalDecision) = StorageHelper.MaxValueAndIndex(valuesForDecisions);

            return (StorageNpv: storageNpv, OptimalInjectWithdraw: decisionSet[indexOfOptimalDecision], 
                    CmdtyConsumedOnAction: cmdtyConsumedForDecisions[indexOfOptimalDecision],
                    ImmediateNpv: immediateNpvs[indexOfOptimalDecision]);
        }

        private TreeSimulationResults<T> CalculateDecisionProfile(TreeStorageValuationResults<T> valuationResults, 
                                                TimeSeries<T, int> spotPricePath)
        {
            double inventory = valuationResults.InventorySpaceGrids[0][0];

            TimeSeries<T, IReadOnlyList<TreeNode>> tree = valuationResults.Tree;
            // TODO put method on TimeSeries class which gets rid of this 2-step validation
            if (spotPricePath.IsEmpty)
                throw new ArgumentException("spotPricePath cannot be an empty Time Series.", nameof(spotPricePath));

            if (!spotPricePath.Start.Equals(tree.Start))
                throw new ArgumentException($"spotPricePath must start on {tree.Start}, the start period of the tree used for valuation.", nameof(spotPricePath));

            if (spotPricePath.End.OffsetFrom(tree.End) < -1)
                throw new ArgumentException($"spotPricePath cannot end earlier than 1 period before {tree.End}, the end period for the tree used for valuation.", nameof(spotPricePath));

            TreeNode treeNode = tree[0][0];
            var decisions = new double[valuationResults.StorageNpvByInventory.Count - 1]; // -1 because StorageNpvByInventory included the end period on which a decision can't be made
            var cmdtyVolumeConsumedArray = new double[valuationResults.StorageNpvByInventory.Count - 1];

            int i = 0;
            double storageNpv = 0.0;
            foreach (T period in tree.Indices.Take(tree.Count - 1))
            {
                if (period.CompareTo(_storage.StartPeriod) >= 0)
                {
                    if (period.Equals(_storage.EndPeriod))
                    {
                        Func<double, double> storageNpvByInventory =
                                        valuationResults.StorageNpvByInventory[period][treeNode.ValueLevelIndex];
                        storageNpv += storageNpvByInventory(inventory);
                    }
                    else
                    {

                        T nextPeriod = period.Offset(1);
                        IReadOnlyList<Func<double, double>> continuationValueByInventory =
                            valuationResults.StorageNpvByInventory[nextPeriod];
                        (double nextStepInventorySpaceMin, double nextStepInventorySpaceMax) =
                            valuationResults.InventorySpace[nextPeriod];

                        double thisStepImmediateNpv;
                        (_, decisions[i], cmdtyVolumeConsumedArray[i], thisStepImmediateNpv) =
                            OptimalDecisionAndValue(_storage, period, inventory, nextStepInventorySpaceMin,
                                nextStepInventorySpaceMax, treeNode, continuationValueByInventory, _settleDateRule,
                                _discountFactors, _numericalTolerance);
                        
                        storageNpv += thisStepImmediateNpv;
                        inventory += decisions[i];
                        i++;
                    }
                }

                int transitionIndex = spotPricePath[period];
                treeNode = treeNode.Transitions[transitionIndex].DestinationNode;
            }

            // TODO once val results decision is trimmed at end, use this for results indices
            var indicesForResults = valuationResults.StorageNpvByInventory.Indices.Take(valuationResults.StorageNpvByInventory.Count - 1);
            var decisionProfile = new DoubleTimeSeries<T>(indicesForResults, decisions);
            var cmdtyConsumed = new DoubleTimeSeries<T>(indicesForResults, cmdtyVolumeConsumedArray);

            return new TreeSimulationResults<T>(storageNpv, decisionProfile, cmdtyConsumed);
        }

        public sealed class DecisionSimulator
        {
            public TreeStorageValuationResults<T> ValuationResults { get; }
            private readonly TreeStorageValuation<T> _storageValuation;

            internal DecisionSimulator(TreeStorageValuationResults<T> valuationResults, TreeStorageValuation<T> storageValuation)
            {
                ValuationResults = valuationResults;
                _storageValuation = storageValuation;
            }

            public TreeSimulationResults<T> SimulateDecisions(TimeSeries<T, int> spotPricePath)
            {
                return _storageValuation.CalculateDecisionProfile(ValuationResults, spotPricePath);
            }

        }

    }


    public interface ITreeAddStartingInventory<T>
    where T : ITimePeriod<T>
    {
        ITreeAddCurrentPeriod<T> WithStartingInventory(double inventory);
    }

    public interface ITreeAddCurrentPeriod<T>
        where T : ITimePeriod<T>
    {
        ITreeAddForwardCurve<T> ForCurrentPeriod(T currentPeriod);
    }

    public interface ITreeAddForwardCurve<T>
        where T : ITimePeriod<T>
    {
        ITreeAddTreeFactory<T> WithForwardCurve(TimeSeries<T, double> forwardCurve);
    }

    public interface ITreeAddTreeFactory<T>
        where T : ITimePeriod<T>
    {
        /// <summary>
        /// Adds a tree factory function to the valuation.
        /// </summary>
        /// <param name="treeFactory">Function mapping from the forward curve to the price tree.</param>
        ITreeAddCmdtySettlementRule<T>
            WithTreeFactory(Func<TimeSeries<T, double>, TimeSeries<T, IReadOnlyList<TreeNode>>> treeFactory);
    }

    public interface ITreeAddCmdtySettlementRule<T>
        where T : ITimePeriod<T>
    {
        /// <summary>
        /// Adds a settlement date rule.
        /// </summary>
        /// <param name="settleDateRule">Function mapping from cmdty delivery date to settlement date.</param>
        ITreeAddDiscountFactorFunc<T> WithCmdtySettlementRule(Func<T, Day> settleDateRule);
    }

    public interface ITreeAddDiscountFactorFunc<T>
        where T : ITimePeriod<T>
    {
        /// <summary>
        /// Adds discount factor function.
        /// </summary>
        /// <param name="discountFactors">Function mapping from cash flow date to discount factor.</param>
        ITreeAddInventoryGridCalculation<T> WithDiscountFactorFunc(Func<Day, double> discountFactors);
    }

    public interface ITreeAddInventoryGridCalculation<T>
        where T : ITimePeriod<T>
    {
        ITreeAddInterpolator<T> WithStateSpaceGridCalculation(Func<CmdtyStorage<T>, IDoubleStateSpaceGridCalc> gridCalcFactory);
    }

    public interface ITreeAddInterpolator<T>
        where T : ITimePeriod<T>
    {
        ITreeAddNumericalTolerance<T> WithInterpolatorFactory(IInterpolatorFactory interpolatorFactory);
    }

    public interface ITreeAddNumericalTolerance<T>
        where T : ITimePeriod<T>
    {
        ITreeCalculate<T> WithNumericalTolerance(double numericalTolerance);
    }
    
    public interface ITreeCalculate<T>
        where T : ITimePeriod<T>
    {
        TreeStorageValuationResults<T> Calculate();
        TreeStorageValuation<T>.DecisionSimulator CalculateDecisionSimulator();
        double CalculateNpv();
    }

}
