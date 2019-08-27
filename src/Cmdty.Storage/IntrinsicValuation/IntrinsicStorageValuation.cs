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
using Cmdty.TimePeriodValueTypes;
using Cmdty.TimeSeries;
using JetBrains.Annotations;

namespace Cmdty.Storage
{
    public sealed class IntrinsicStorageValuation<T> : IIntrinsicAddStartingInventory<T>, IIntrinsicAddCurrentPeriod<T>, IIntrinsicAddForwardCurve<T>, 
            IIntrinsicAddCmdtySettlementRule<T>, IIntrinsicAddDiscountFactorFunc<T>, IIntrinsicAddSpacing<T>, IIntrinsicNumericalTolerance<T>, IIntrinsicAddInterpolatorOrCalculate<T>
        where T : ITimePeriod<T>
    {
        private readonly CmdtyStorage<T> _storage;
        private double _startingInventory;
        private T _currentPeriod;
        private TimeSeries<T, double> _forwardCurve;
        private Func<T, Day> _settleDateRule;
        private Func<Day, double> _discountFactors;
        private IDoubleStateSpaceGridCalc _gridCalc;
        private IInterpolatorFactory _interpolatorFactory;
        private double _gridSpacing = 100;
        private double _numericalTolerance;

        private IntrinsicStorageValuation([NotNull] CmdtyStorage<T> storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public static IIntrinsicAddStartingInventory<T> ForStorage([NotNull] CmdtyStorage<T> storage)
        {
            return new IntrinsicStorageValuation<T>(storage);
        }

        IIntrinsicAddCurrentPeriod<T> IIntrinsicAddStartingInventory<T>.WithStartingInventory(double inventory)
        {
            if (inventory < 0)
                throw new ArgumentException("Inventory cannot be negative", nameof(inventory));
            _startingInventory = inventory;
            return this;
        }

        IIntrinsicAddForwardCurve<T> IIntrinsicAddCurrentPeriod<T>.ForCurrentPeriod([NotNull] T currentPeriod)
        {
            if (currentPeriod == null)
                throw new ArgumentNullException(nameof(currentPeriod));
            _currentPeriod = currentPeriod;
            return this;
        }

        IIntrinsicAddCmdtySettlementRule<T> IIntrinsicAddForwardCurve<T>.WithForwardCurve([NotNull] TimeSeries<T, double> forwardCurve)
        {
            _forwardCurve = forwardCurve ?? throw new ArgumentNullException(nameof(forwardCurve));
            return this;
        }

        public IIntrinsicAddDiscountFactorFunc<T> WithCmdtySettlementRule([NotNull] Func<T, Day> settleDateRule)
        {
            _settleDateRule = settleDateRule ?? throw new ArgumentNullException(nameof(settleDateRule));
            return this;
        }

        IIntrinsicAddSpacing<T> IIntrinsicAddDiscountFactorFunc<T>.WithDiscountFactorFunc([NotNull] Func<Day, double> discountFactors)
        {
            _discountFactors = discountFactors ?? throw new ArgumentNullException(nameof(discountFactors));
            return this;
        }

        IIntrinsicNumericalTolerance<T> IIntrinsicAddSpacing<T>.WithGridSpacing(double gridSpacing)
        {
            if (gridSpacing <= 0.0)
                throw new ArgumentException($"Parameter {nameof(gridSpacing)} value must be positive", nameof(gridSpacing));
            _gridSpacing = gridSpacing;
            return this;
        }

        IIntrinsicNumericalTolerance<T> IIntrinsicAddSpacing<T>
                    .WithStateSpaceGridCalculation([NotNull] IDoubleStateSpaceGridCalc gridCalc)
        {
            _gridCalc = gridCalc ?? throw new ArgumentNullException(nameof(gridCalc));
            return this;
        }

        IIntrinsicAddInterpolatorOrCalculate<T> IIntrinsicNumericalTolerance<T>.WithNumericalTolerance(double numericalTolerance)
        {
            if (numericalTolerance <= 0)
                throw new ArgumentException("Numerical tolerance must be positive.", nameof(numericalTolerance));
            _numericalTolerance = numericalTolerance;
            return this;
        }

        IIntrinsicAddInterpolatorOrCalculate<T> IIntrinsicAddInterpolatorOrCalculate<T>
                    .WithInterpolatorFactory([NotNull] IInterpolatorFactory interpolatorFactory)
        {
            _interpolatorFactory = interpolatorFactory ?? throw new ArgumentNullException(nameof(interpolatorFactory));
            return this;
        }

        IntrinsicStorageValuationResults<T> IIntrinsicAddInterpolatorOrCalculate<T>.Calculate()
        {
            return Calculate(_currentPeriod, _startingInventory, _forwardCurve, _storage, _settleDateRule, _discountFactors,
                    _gridCalc ?? new FixedSpacingStateSpaceGridCalc(_gridSpacing),
                    _interpolatorFactory ?? new LinearInterpolatorFactory(), _numericalTolerance);
        }

        private static IntrinsicStorageValuationResults<T> Calculate(T currentPeriod, double startingInventory,
                TimeSeries<T, double> forwardCurve, CmdtyStorage<T> storage, Func<T, Day> settleDateRule,
                Func<Day, double> discountFactors, IDoubleStateSpaceGridCalc gridCalc,
                IInterpolatorFactory interpolatorFactory, double numericalTolerance)
        {
            if (startingInventory < 0)
                throw new ArgumentException("Inventory cannot be negative.", nameof(startingInventory));

            if (currentPeriod.CompareTo(storage.EndPeriod) > 0)
                return new IntrinsicStorageValuationResults<T>(0.0, DoubleTimeSeries<T>.Empty);

            if (currentPeriod.Equals(storage.EndPeriod))
            {
                if (storage.MustBeEmptyAtEnd)
                {
                    if (startingInventory > 0) // TODO allow some tolerance for floating point numerical error?
                        throw new InventoryConstraintsCannotBeFulfilledException("Storage must be empty at end, but inventory is greater than zero.");
                    return new IntrinsicStorageValuationResults<T>(0.0, DoubleTimeSeries<T>.Empty);
                }

                double terminalMinInventory = storage.MinInventory(storage.EndPeriod);
                double terminalMaxInventory = storage.MaxInventory(storage.EndPeriod);

                if (startingInventory < terminalMinInventory)
                    throw new InventoryConstraintsCannotBeFulfilledException("Current inventory is lower than the minimum allowed in the end period.");

                if (startingInventory > terminalMaxInventory)
                    throw new InventoryConstraintsCannotBeFulfilledException("Current inventory is greater than the maximum allowed in the end period.");

                double cmdtyPrice = forwardCurve[storage.EndPeriod];
                double npv = storage.TerminalStorageNpv(cmdtyPrice, startingInventory);
                return new IntrinsicStorageValuationResults<T>(npv, DoubleTimeSeries<T>.Empty);
            }

            TimeSeries<T, InventoryRange> inventorySpace = StorageHelper.CalculateInventorySpace(storage, startingInventory, currentPeriod);

            // TODO think of method to put in TimeSeries class to perform the validation check below in one line
            if (forwardCurve.IsEmpty)
                throw new ArgumentException("Forward curve cannot be empty.", nameof(forwardCurve));

            if (forwardCurve.Start.CompareTo(inventorySpace.Start) > 0)
                throw new ArgumentException("Forward curve starts too late.", nameof(forwardCurve));

            if (forwardCurve.End.CompareTo(inventorySpace.End) < 0)
                throw new ArgumentException("Forward curve does not extend until storage end period.", nameof(forwardCurve));

            // Perform backward induction
            var storageValueByInventory = new Func<double, double>[inventorySpace.Count];

            double cmdtyPriceAtEnd = forwardCurve[storage.EndPeriod];
            storageValueByInventory[inventorySpace.Count - 1] = 
                finalInventory => storage.TerminalStorageNpv(cmdtyPriceAtEnd, finalInventory) ;

            int backCounter = inventorySpace.Count - 2;
            foreach (T periodLoop in inventorySpace.Indices.Reverse().Skip(1))
            {
                (double inventorySpaceMin, double inventorySpaceMax) = inventorySpace[periodLoop];
                double[] inventorySpaceGrid = gridCalc.GetGridPoints(inventorySpaceMin, inventorySpaceMax)
                                                        .ToArray();
                var storageValuesGrid = new double[inventorySpaceGrid.Length];

                double cmdtyPrice = forwardCurve[periodLoop];
                Func<double, double> continuationValueByInventory = storageValueByInventory[backCounter + 1];
                
                (double nextStepInventorySpaceMin, double nextStepInventorySpaceMax) = inventorySpace[periodLoop.Offset(1)];
                for (int i = 0; i < inventorySpaceGrid.Length; i++)
                {
                    double inventory = inventorySpaceGrid[i];
                    storageValuesGrid[i] = OptimalDecisionAndValue(storage, periodLoop, inventory, nextStepInventorySpaceMin, 
                                                nextStepInventorySpaceMax, cmdtyPrice, continuationValueByInventory, 
                                                settleDateRule, discountFactors, numericalTolerance).StorageNpv;
                }

                storageValueByInventory[backCounter] =
                    interpolatorFactory.CreateInterpolator(inventorySpaceGrid, storageValuesGrid);
                backCounter--;
            }

            // Loop forward from start inventory choosing optimal decisions
            double storageNpv = 0.0;

            var decisionProfileBuilder = new DoubleTimeSeries<T>.Builder(inventorySpace.Count);

            double inventoryLoop = startingInventory;
            for (int i = 0; i < inventorySpace.Count; i++)
            {
                T periodLoop = currentPeriod.Offset(i);
                double cmdtyPrice = forwardCurve[periodLoop];
                Func<double, double> continuationValueByInventory = storageValueByInventory[i];
                (double nextStepInventorySpaceMin, double nextStepInventorySpaceMax) = inventorySpace[periodLoop.Offset(1)];
                (double storageNpvLoop, double optimalInjectWithdraw) = OptimalDecisionAndValue(storage, periodLoop, inventoryLoop, nextStepInventorySpaceMin,
                                        nextStepInventorySpaceMax, cmdtyPrice, continuationValueByInventory, settleDateRule, discountFactors, numericalTolerance);
                decisionProfileBuilder.Add(periodLoop, optimalInjectWithdraw);
                inventoryLoop += optimalInjectWithdraw;
                if (i == 0)
                {
                    storageNpv = storageNpvLoop;
                }
            }

            return new IntrinsicStorageValuationResults<T>(storageNpv, decisionProfileBuilder.Build());
        }

        private static (double StorageNpv, double OptimalInjectWithdraw) OptimalDecisionAndValue(CmdtyStorage<T> storage, T periodLoop, double inventory,
            double nextStepInventorySpaceMin, double nextStepInventorySpaceMax, double cmdtyPrice,
            Func<double, double> continuationValueByInventory, Func<T, Day> settleDateRule, Func<Day, double> discountFactors, 
            double numericalTolerance)
        {
            InjectWithdrawRange injectWithdrawRange = storage.GetInjectWithdrawRange(periodLoop, inventory);
            double[] decisionSet = StorageHelper.CalculateBangBangDecisionSet(injectWithdrawRange, inventory,
                                                    nextStepInventorySpaceMin, nextStepInventorySpaceMax, numericalTolerance);
            var valuesForDecision = new double[decisionSet.Length];
            for (var j = 0; j < decisionSet.Length; j++)
            {
                double decisionInjectWithdraw = decisionSet[j];
                valuesForDecision[j] = StorageValueForDecision(storage, periodLoop, inventory,
                    decisionInjectWithdraw, cmdtyPrice, continuationValueByInventory, settleDateRule, discountFactors);
            }

            (double storageNpv, int indexOfOptimalDecision) = StorageHelper.MaxValueAndIndex(valuesForDecision);

            return (StorageNpv: storageNpv, OptimalInjectWithdraw: decisionSet[indexOfOptimalDecision]);
        }



        private static double StorageValueForDecision(CmdtyStorage<T> storage, T period, double inventory,
                        double injectWithdrawVolume, double cmdtyPrice, Func<double, double> continuationValueInterpolated, 
                        Func<T, Day> settleDateRule, Func<Day, double> discountFactors)
        {
            double inventoryAfterDecision = inventory + injectWithdrawVolume;
            double continuationFutureNpv = continuationValueInterpolated(inventoryAfterDecision);

            Day cmdtySettlementDate = settleDateRule(period);
            double discountFactorFromCmdtySettlement = discountFactors(cmdtySettlementDate);

            double injectWithdrawNpv = -injectWithdrawVolume * cmdtyPrice * discountFactorFromCmdtySettlement;

            IReadOnlyList<DomesticCashFlow> storageCostCashFlows = injectWithdrawVolume > 0.0
                    ? storage.InjectionCost(period, inventory, injectWithdrawVolume)
                    : storage.WithdrawalCost(period, inventory, -injectWithdrawVolume);

            double storageCostNpv = storageCostCashFlows.Sum(cashFlow => cashFlow.Amount * discountFactors(cashFlow.Date));

            double cmdtyUsedForInjectWithdrawVolume = injectWithdrawVolume > 0.0
                ? storage.CmdtyVolumeConsumedOnInject(period, inventory, injectWithdrawVolume)
                : storage.CmdtyVolumeConsumedOnWithdraw(period, inventory, Math.Abs(injectWithdrawVolume));

            double cmdtyUsedForInjectWithdrawNpv = -cmdtyUsedForInjectWithdrawVolume * cmdtyPrice * discountFactorFromCmdtySettlement;

            return continuationFutureNpv + injectWithdrawNpv - storageCostNpv + cmdtyUsedForInjectWithdrawNpv;
        }

    }

    public interface IIntrinsicAddStartingInventory<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicAddCurrentPeriod<T> WithStartingInventory(double inventory);
    }

    public interface IIntrinsicAddCurrentPeriod<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicAddForwardCurve<T> ForCurrentPeriod(T currentPeriod);
    }

    public interface IIntrinsicAddForwardCurve<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicAddCmdtySettlementRule<T> WithForwardCurve(TimeSeries<T, double> forwardCurve);
    }

    public interface IIntrinsicAddCmdtySettlementRule<T>
        where T : ITimePeriod<T>
    {
        /// <summary>
        /// Adds a settlement date rule.
        /// </summary>
        /// <param name="settleDateRule">Function mapping from cmdty delivery date to settlement date.</param>
        IIntrinsicAddDiscountFactorFunc<T> WithCmdtySettlementRule(Func<T, Day> settleDateRule);
    }

    public interface IIntrinsicAddDiscountFactorFunc<T>
        where T : ITimePeriod<T>
    {
        /// <summary>
        /// Adds discount factor function.
        /// </summary>
        /// <param name="discountFactors">Function mapping from cash flow date to discount factor.</param>
        IIntrinsicAddSpacing<T> WithDiscountFactorFunc(Func<Day, double> discountFactors);
    }

    public interface IIntrinsicAddSpacing<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicNumericalTolerance<T> WithGridSpacing(double gridSpacing);
        IIntrinsicNumericalTolerance<T> WithStateSpaceGridCalculation(IDoubleStateSpaceGridCalc gridCalc);
    }

    public interface IIntrinsicNumericalTolerance<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicAddInterpolatorOrCalculate<T> WithNumericalTolerance(double numericalTolerance);
    }

    public interface IIntrinsicAddInterpolatorOrCalculate<T>
        where T : ITimePeriod<T>
    {
        IIntrinsicAddInterpolatorOrCalculate<T> WithInterpolatorFactory(IInterpolatorFactory interpolatorFactory);
        IntrinsicStorageValuationResults<T> Calculate();
    }

}
