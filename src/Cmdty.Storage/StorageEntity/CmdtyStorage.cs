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
using Cmdty.TimePeriodValueTypes;
using JetBrains.Annotations;

namespace Cmdty.Storage
{
    /// <summary>
    /// Represents ownership of a commodity storage facility, either virtual or physical.
    /// </summary>
    public sealed class CmdtyStorage<T>
        where T : ITimePeriod<T>
    {
        private readonly Func<T, IInjectWithdrawConstraint> _injectWithdrawConstraints;
        private readonly Func<T, double> _maxInventory;
        private readonly Func<T, double> _minInventory;
        private readonly Func<T, double, double, IReadOnlyList<DomesticCashFlow>> _injectionCashFlows;
        private readonly Func<T, double, double, double> _injectCmdtyConsumed;
        private readonly Func<T, double, double, IReadOnlyList<DomesticCashFlow>> _withdrawalCashFlows;
        private readonly Func<T, double, double, double> _withdrawCmdtyConsumed;

        private readonly Func<double, double, double> _terminalStorageValue;
        public bool MustBeEmptyAtEnd { get; }

        private CmdtyStorage(T startPeriod,
                            T endPeriod,
                            Func<T, IInjectWithdrawConstraint> injectWithdrawConstraints,
                            Func<T, double> maxInventory,
                            Func<T, double> minInventory,
                            Func<T, double, double, IReadOnlyList<DomesticCashFlow>> injectionCashFlows,
                            Func<T, double, double, IReadOnlyList<DomesticCashFlow>> withdrawalCashFlows,
                            Func<double, double, double> terminalStorageValue,
                            bool mustBeEmptyAtEnd,
                            Func<T, double, double, double> injectCmdtyConsumed,
                            Func<T, double, double, double> withdrawCmdtyConsumed)
        {
            StartPeriod = startPeriod;
            EndPeriod = endPeriod;
            _injectWithdrawConstraints = injectWithdrawConstraints;
            _maxInventory = maxInventory;
            _minInventory = minInventory;
            _injectionCashFlows = injectionCashFlows;
            _withdrawalCashFlows = withdrawalCashFlows;
            _terminalStorageValue = terminalStorageValue;
            MustBeEmptyAtEnd = mustBeEmptyAtEnd;
            _injectCmdtyConsumed = injectCmdtyConsumed;
            _withdrawCmdtyConsumed = withdrawCmdtyConsumed;
        }

        public T StartPeriod { get; }
        public T EndPeriod { get; }
        
        public InjectWithdrawRange GetInjectWithdrawRange(T date, double inventory)
        {
            double minInventory = _minInventory(date);
            if (inventory < minInventory)
                throw new ArgumentException($"Inventory is below minimum allowed value of {minInventory} during period {date}.", nameof(inventory));

            double maxInventory = _maxInventory(date);
            if (inventory > maxInventory)
                throw new ArgumentException($"Inventory is above maximum allowed value of {maxInventory} during period {date}.", nameof(inventory));

            if (date.CompareTo(EndPeriod) >= 0)
                return new InjectWithdrawRange(0.0, 0.0);

            return _injectWithdrawConstraints(date).GetInjectWithdrawRange(inventory);
        }
        
        public double MaxInventory(T date)
        {
            return _maxInventory(date);
        }

        public double MinInventory(T date)
        {
            return _minInventory(date);
        }

        public IReadOnlyList<DomesticCashFlow> InjectionCost(T date, double inventory, double injectedVolume)
        {
            return _injectionCashFlows(date, inventory, injectedVolume);
        }

        public double CmdtyVolumeConsumedOnInject(T date, double inventory, double injectedVolume)
        {
            return _injectCmdtyConsumed(date, inventory, injectedVolume);
        }

        public IReadOnlyList<DomesticCashFlow> WithdrawalCost(T date, double inventory, double withdrawnVolume)
        {
            return _withdrawalCashFlows(date, inventory, withdrawnVolume);
        }

        public double CmdtyVolumeConsumedOnWithdraw(T date, double inventory, double withdrawnVolume)
        {
            return _withdrawCmdtyConsumed(date, inventory, withdrawnVolume);
        }

        public double InventorySpaceUpperBound([NotNull] T period, double nextPeriodInventorySpaceLowerBound, double nextPeriodInventorySpaceUpperBound)
        {
            if (period == null) throw new ArgumentNullException(nameof(period));
            IInjectWithdrawConstraint injectWithdrawConstraint = _injectWithdrawConstraints(period);
            double inventorySpaceUpper =
                injectWithdrawConstraint.InventorySpaceUpperBound(nextPeriodInventorySpaceLowerBound, nextPeriodInventorySpaceUpperBound, MinInventory(period), MaxInventory(period));
            return inventorySpaceUpper;
        }

        public double InventorySpaceLowerBound([NotNull] T period, double nextPeriodInventorySpaceLowerBound, double nextPeriodInventorySpaceUpperBound)
        {
            if (period == null) throw new ArgumentNullException(nameof(period));
            IInjectWithdrawConstraint injectWithdrawConstraint = _injectWithdrawConstraints(period);
            double inventorySpaceLower =
                injectWithdrawConstraint.InventorySpaceLowerBound(nextPeriodInventorySpaceLowerBound, nextPeriodInventorySpaceUpperBound, MinInventory(period), MaxInventory(period));
            return inventorySpaceLower;
        }

        public double TerminalStorageNpv(double cmdtyPrice, double finalInventory)
        {
            return _terminalStorageValue(cmdtyPrice, finalInventory);
        }

        public static IBuilder<T> Builder => new StorageBuilder();

        private sealed class StorageBuilder : IBuilder<T>, IAddInjectWithdrawConstraints, IAddMaxInventory, IAddMinInventory, IAddInjectionCost, 
                    IAddWithdrawalCost, IAddTerminalStorageState, IBuildCmdtyStorage, IAddCmdtyConsumedOnInject, IAddCmdtyConsumedOnWithdraw
        {
            private T _startPeriod;
            private T _endPeriod;
            private Func<T, IInjectWithdrawConstraint> _injectWithdrawConstraints;
            private Func<T, double> _maxInventory;
            private Func<T, double> _minInventory;
            private Func<T, double, double, IReadOnlyList<DomesticCashFlow>> _injectionCashFlows;
            private Func<T, double, double, IReadOnlyList<DomesticCashFlow>> _withdrawalCashFlows;
            private Func<double, double, double> _terminalStorageValue;
            private bool _mustBeEmptyAtEnd;
            private Func<T, double, double, double> _injectCmdtyConsumed;
            private Func<T, double, double, double> _withdrawCmdtyConsumed;
            
            IAddInjectWithdrawConstraints IBuilder<T>.WithActiveTimePeriod(T start, T end)
            {
                if (start.CompareTo(end) >= 0)
                    throw new ArgumentException("Storage start period must be before end period.");
                _startPeriod = start;
                _endPeriod = end;
                return this;
            }

            IAddMinInventory IAddInjectWithdrawConstraints.WithTimeDependentInjectWithdrawRange(Func<T, InjectWithdrawRange> injectWithdrawRangeByPeriod)
            {
                if (injectWithdrawRangeByPeriod == null) throw new ArgumentNullException(nameof(injectWithdrawRangeByPeriod));
                _injectWithdrawConstraints = period => new ConstantInjectWithdrawConstraint(injectWithdrawRangeByPeriod(period));
                return this;
            }

            IAddMinInventory IAddInjectWithdrawConstraints.WithInjectWithdrawConstraint(IInjectWithdrawConstraint injectWithdrawConstraint)
            {
                if (injectWithdrawConstraint == null) throw new ArgumentNullException(nameof(injectWithdrawConstraint));
                _injectWithdrawConstraints = date => injectWithdrawConstraint;
                return this;
            }

            IAddMinInventory IAddInjectWithdrawConstraints.WithInjectWithdrawConstraint(Func<T, IInjectWithdrawConstraint> injectWithdrawConstraintByPeriod)
            {
                _injectWithdrawConstraints = injectWithdrawConstraintByPeriod ?? throw new ArgumentNullException(nameof(injectWithdrawConstraintByPeriod));
                return this;
            }

            IAddInjectionCost IAddMaxInventory.WithConstantMaxInventory(double maxInventory)
            {
                if (maxInventory < 0)
                    throw new ArgumentException("Maximum inventory must be non-negative.", nameof(maxInventory));

                _maxInventory = date => maxInventory;
                return this;
            }

            IAddInjectionCost IAddMaxInventory.WithMaxInventory(Func<T, double> maxInventory)
            {
                _maxInventory = maxInventory ?? throw new ArgumentNullException(nameof(maxInventory));
                return this;
            }

            IAddMaxInventory IAddMinInventory.WithZeroMinInventory()
            {
                _minInventory = date => 0.0;
                return this;
            }

            IAddMaxInventory IAddMinInventory.WithConstantMinInventory(double minInventory)
            {
                if (minInventory < 0)
                    throw new ArgumentException("Minimum inventory must be non-negative.", nameof(minInventory));

                _minInventory = date => minInventory;
                return this;
            }

            IAddMaxInventory IAddMinInventory.WithMinInventory(Func<T, double> minInventory)
            {
                _minInventory = minInventory ?? throw new ArgumentNullException(nameof(minInventory));
                return this;
            }

            IAddCmdtyConsumedOnInject IAddInjectionCost.WithPerUnitInjectionCost(double perVolumeUnitCost,
                                                    [NotNull] Func<T, Day> cashFlowDate)
            {
                if (cashFlowDate == null) throw new ArgumentNullException(nameof(cashFlowDate));
                if (perVolumeUnitCost < 0)
                    throw new ArgumentException("Per unit inject cost must be non-negative.", nameof(perVolumeUnitCost));

                _injectionCashFlows = (date, inventory, injectedVolume) 
                    => new [] {new DomesticCashFlow(cashFlowDate(date), perVolumeUnitCost * injectedVolume)};
                return this;
            }

            IAddCmdtyConsumedOnInject IAddInjectionCost.WithInjectionCost(
                Func<T, double, double, IReadOnlyList<DomesticCashFlow>> injectionCost)
            {
                _injectionCashFlows = injectionCost ?? throw new ArgumentNullException(nameof(injectionCost));
                return this;
            }

            IAddCmdtyConsumedOnWithdraw IAddWithdrawalCost.WithPerUnitWithdrawalCost(double perVolumeUnitCost,
                                                    [NotNull] Func<T, Day> cashFlowDate)
            {
                if (cashFlowDate == null) throw new ArgumentNullException(nameof(cashFlowDate));
                if (perVolumeUnitCost < 0)
                    throw new ArgumentException("Per unit inject cost must be non-negative.", nameof(perVolumeUnitCost));

                _withdrawalCashFlows = (date, inventory, withdrawnVolume) 
                    => new[] { new DomesticCashFlow(cashFlowDate(date), perVolumeUnitCost * Math.Abs(withdrawnVolume)) };
                return this;
            }

            IAddCmdtyConsumedOnWithdraw IAddWithdrawalCost.WithWithdrawalCost(
                Func<T, double, double, IReadOnlyList<DomesticCashFlow>> withdrawalCost)
            {
                _withdrawalCashFlows = withdrawalCost ?? throw new ArgumentNullException(nameof(withdrawalCost));
                return this;
            }
            
            IBuildCmdtyStorage IAddTerminalStorageState.WithTerminalInventoryNpv([NotNull] Func<double, double, double> terminalStorageValueFunc)
            {
                _terminalStorageValue = terminalStorageValueFunc ?? throw new ArgumentNullException(nameof(terminalStorageValueFunc));
                return this;
            }

            IBuildCmdtyStorage IAddTerminalStorageState.MustBeEmptyAtEnd()
            {
                _mustBeEmptyAtEnd = true;
                return this;
            }

            CmdtyStorage<T> IBuildCmdtyStorage.Build()
            {
                Func<double, double, double> terminalStorageValue =_terminalStorageValue ?? ((cmdtyPrice, finalInventory) => 0.0);

                Func<T, double> maxInventory;
                if (_mustBeEmptyAtEnd)
                {
                    maxInventory = period => period.CompareTo(_endPeriod) >= 0 ? 0.0 : _maxInventory(period);
                }
                else
                {
                    maxInventory = _maxInventory;
                }

                return new CmdtyStorage<T>(_startPeriod, _endPeriod, _injectWithdrawConstraints, maxInventory, 
                            _minInventory, _injectionCashFlows, _withdrawalCashFlows, terminalStorageValue, _mustBeEmptyAtEnd, 
                            _injectCmdtyConsumed, _withdrawCmdtyConsumed);
            }

            IAddWithdrawalCost IAddCmdtyConsumedOnInject.WithNoCmdtyConsumedOnInject()
            {
                _injectCmdtyConsumed = (period, inventory, injectedVolume) => 0.0;
                return this;
            }

            IAddWithdrawalCost IAddCmdtyConsumedOnInject.WithFixedPercentCmdtyConsumedOnInject(double percentCmdtyConsumed)
            {
                _injectCmdtyConsumed = (period, inventory, injectedVolume) => percentCmdtyConsumed * Math.Abs(injectedVolume);
                return this;
            }

            IAddWithdrawalCost IAddCmdtyConsumedOnInject.WithCmdtyConsumedOnInject(
                            [NotNull] Func<T, double, double, double> volumeOfCmdtyConsumed)
            {
                _injectCmdtyConsumed = volumeOfCmdtyConsumed ?? throw new ArgumentNullException(nameof(volumeOfCmdtyConsumed));
                return this;
            }

            IAddTerminalStorageState IAddCmdtyConsumedOnWithdraw.WithNoCmdtyConsumedOnWithdraw()
            {
                _withdrawCmdtyConsumed = (period, inventory, withdrawnVolume) => 0.0;
                return this;
            }

            IAddTerminalStorageState IAddCmdtyConsumedOnWithdraw.WithFixedPercentCmdtyConsumedOnWithdraw(double percentCmdtyConsumed)
            {
                _withdrawCmdtyConsumed = (period, inventory, withdrawnVolume) => percentCmdtyConsumed * Math.Abs(withdrawnVolume);
                return this;
            }

            IAddTerminalStorageState IAddCmdtyConsumedOnWithdraw.WithCmdtyConsumedOnWithdraw(
                                [NotNull] Func<T, double, double, double> volumeOfCmdtyConsumed)
            {
                _withdrawCmdtyConsumed = volumeOfCmdtyConsumed ?? throw new ArgumentNullException(nameof(volumeOfCmdtyConsumed));
                return this;
            }

        }

        public interface IAddInjectWithdrawConstraints
        {
            IAddMinInventory WithTimeDependentInjectWithdrawRange(Func<T, InjectWithdrawRange> injectWithdrawRangeByPeriod);
            IAddMinInventory WithInjectWithdrawConstraint(IInjectWithdrawConstraint injectWithdrawConstraint);
            IAddMinInventory WithInjectWithdrawConstraint(Func<T, IInjectWithdrawConstraint> injectWithdrawConstraintByPeriod);
        }

        public interface IAddMinInventory
        {
            IAddMaxInventory WithZeroMinInventory();
            IAddMaxInventory WithConstantMinInventory(double minInventory);
            IAddMaxInventory WithMinInventory(Func<T, double> minInventory);
        }

        public interface IAddMaxInventory
        {
            IAddInjectionCost WithConstantMaxInventory(double maxInventory);
            IAddInjectionCost WithMaxInventory(Func<T, double> maxInventory);
        }
        
        public interface IAddInjectionCost
        {
            IAddCmdtyConsumedOnInject WithPerUnitInjectionCost(double perVolumeUnitCost, Func<T, Day> cashFlowDate);
            /// <summary>
            /// Adds the inject cost rule.
            /// </summary>
            /// <param name="injectionCost">Function mapping from the period, inventory (before injection) and
            /// injected volume to the cost cash flows incurred for injecting this volume.</param>
            IAddCmdtyConsumedOnInject WithInjectionCost(Func<T, double, double, IReadOnlyList<DomesticCashFlow>> injectionCost);
        }

        public interface IAddCmdtyConsumedOnInject
        {
            IAddWithdrawalCost WithNoCmdtyConsumedOnInject();
            IAddWithdrawalCost WithFixedPercentCmdtyConsumedOnInject(double percentCmdtyConsumed);
            IAddWithdrawalCost WithCmdtyConsumedOnInject(Func<T, double, double, double> volumeOfCmdtyConsumed);
        }

        public interface IAddWithdrawalCost
        {
            IAddCmdtyConsumedOnWithdraw WithPerUnitWithdrawalCost(double withdrawalCost, Func<T, Day> cashFlowDate);
            /// <summary>
            /// Adds the withdrawal cost rule.
            /// </summary>
            /// <param name="withdrawalCost">Function mapping from the period, inventory (before withdrawal) and
            /// withdrawn volume to the cost cash flows incurred for withdrawing this volume.</param>
            IAddCmdtyConsumedOnWithdraw WithWithdrawalCost(Func<T, double, double, IReadOnlyList<DomesticCashFlow>> withdrawalCost);
        }

        public interface IAddCmdtyConsumedOnWithdraw
        {
            IAddTerminalStorageState WithNoCmdtyConsumedOnWithdraw();
            IAddTerminalStorageState WithFixedPercentCmdtyConsumedOnWithdraw(double percentCmdtyConsumed);
            IAddTerminalStorageState WithCmdtyConsumedOnWithdraw(Func<T, double, double, double> volumeOfCmdtyConsumed);
        }

        public interface IAddTerminalStorageState
        {
            /// <summary>
            /// Adds rule of NPV for any inventory left in storage at the end, should this be allowed.
            /// </summary>
            /// <param name="terminalStorageValueFunc">Function mapping cmdty price and final inventory on the end period
            /// to the NPV.</param>
            IBuildCmdtyStorage WithTerminalInventoryNpv(Func<double, double, double> terminalStorageValueFunc);
            IBuildCmdtyStorage MustBeEmptyAtEnd();
        }

        public interface IBuildCmdtyStorage
        {
            CmdtyStorage<T> Build();
        }

    }
    public interface IBuilder<T>
        where T : ITimePeriod<T>
    {
        CmdtyStorage<T>.IAddInjectWithdrawConstraints WithActiveTimePeriod(T start, T end);
    }

}
