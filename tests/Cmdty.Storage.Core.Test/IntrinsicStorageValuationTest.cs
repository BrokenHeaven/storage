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

using Cmdty.TimePeriodValueTypes;
using Cmdty.TimeSeries;
using Xunit;
// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local

namespace Cmdty.Storage.Core.Test
{
    public sealed class IntrinsicStorageValuationTest
    {

        private static IntrinsicStorageValuationResults<Day> GenerateValuationResults(double startingInventory, 
                                                                        TimeSeries<Day, double> forwardCurve, Day currentPeriod)
        {
            var storageStart = new Day(2019, 9, 1);
            var storageEnd = new Day(2019, 9, 30);

            CmdtyStorage<Day> storage = CmdtyStorage<Day>.Builder
                .WithActiveTimePeriod(storageStart, storageEnd)
                .WithConstantInjectWithdrawRange(-45.5, 56.6)
                .WithConstantMaxInventory(1000.0)
                .WithConstantMinInventory(0.0)
                .WithPerUnitInjectionCost(0.8)
                .WithPerUnitWithdrawalCost(1.2)
                .MustBeEmptyAtEnd()
                .Build();

            IntrinsicStorageValuationResults<Day> valuationResults = new IntrinsicStorageValuation<Day>()
                .ForStorage(storage)
                .WithStartingInventory(startingInventory)
                .ForCurrentPeriod(currentPeriod)
                .WithForwardCurve(forwardCurve)
                .WithDiscountFactorFunc(day => 1.0)
                .WithGridSpacing(10.0)
                .Calculate();

            return valuationResults;
        }

        private static TimeSeries<Day, double> GenerateBackwardatedCurve(Day storageStart, Day storageEnd)
        {
            var forwardCurveBuilder = new TimeSeries<Day, double>.Builder();
            double forwardPrice = 59.95;
            foreach (Day forwardCurvePoint in storageStart.EnumerateTo(storageEnd))
            {
                forwardCurveBuilder.Add(forwardCurvePoint, forwardPrice);
                forwardPrice -= 0.58;
            }

            return forwardCurveBuilder.Build();
        }

        private void AssertDecisionProfileAllZeros<T>(DoubleTimeSeries<T> decisionProfile, T expectedStart, T expectedEnd)
            where T : ITimePeriod<T>
        {
            Assert.Equal(expectedStart, decisionProfile.Start);
            Assert.Equal(expectedEnd, decisionProfile.End);
            foreach (double d in decisionProfile.Data)
            {
                Assert.Equal(0.0, d);
            }
        }

        [Fact]
        public void Calculate_ZeroInventoryCurveBackwardated_ResultWithZeroNetPresentValue()
        {
            var currentPeriod = new Day(2019, 9, 15);

            var forwardCurve = GenerateBackwardatedCurve(new Day(2019, 9, 1), 
                                                        new Day(2019, 9, 30));

            IntrinsicStorageValuationResults<Day> valuationResults = GenerateValuationResults(0.0, forwardCurve, currentPeriod);

            Assert.Equal(0.0, valuationResults.NetPresentValue);
        }

        [Fact]
        public void Calculate_ZeroInventoryCurveBackwardated_ResultWithZerosDecisionProfile()
        {
            var currentPeriod = new Day(2019, 9, 15);

            var forwardCurve = GenerateBackwardatedCurve(new Day(2019, 9, 1),
                                    new Day(2019, 9, 30));

            IntrinsicStorageValuationResults<Day> valuationResults = GenerateValuationResults(0.0, forwardCurve, currentPeriod);

            AssertDecisionProfileAllZeros(valuationResults.DecisionProfile, 
                                new Day(2019, 9, 15), 
                                new Day(2019, 9, 29));
        }

        [Fact]
        public void Calculate_ZeroInventoryForwardSpreadLessThanCycleCost_ResultWithZeroNetPresentValue()
        {
            // Cycle cost = 2.0, so create curve with spread of 1.99
            var valuationResults = IntrinsicValuationZeroInventoryForwardCurveWithSpread(1.99);

            Assert.Equal(0.0, valuationResults.NetPresentValue);
        }

        [Fact]
        public void Calculate_ZeroInventoryForwardSpreadLessThanCycleCost_ResultWithZerosDecisionProfile()
        {
            // Cycle cost = 2.0, so create curve with spread of 1.99
            var valuationResults = IntrinsicValuationZeroInventoryForwardCurveWithSpread(1.99);

            AssertDecisionProfileAllZeros(valuationResults.DecisionProfile,
                                new Day(2019, 9, 15),
                                new Day(2019, 9, 29));
        }

        private static IntrinsicStorageValuationResults<Day> IntrinsicValuationZeroInventoryForwardCurveWithSpread(double forwardSpread)
        {
            var currentPeriod = new Day(2019, 9, 15);
            const double lowerForwardPrice = 56.6;
            double higherForwardPrice = lowerForwardPrice + forwardSpread;

            var forwardCurveBuilder = new TimeSeries<Day, double>.Builder();

            foreach (var day in new Day(2019, 9, 15).EnumerateTo(new Day(2019, 9, 22)))
            {
                forwardCurveBuilder.Add(day, lowerForwardPrice);
            }

            foreach (var day in new Day(2019, 9, 23).EnumerateTo(new Day(2019, 9, 30)))
            {
                forwardCurveBuilder.Add(day, higherForwardPrice);
            }

            IntrinsicStorageValuationResults<Day> valuationResults = GenerateValuationResults(0.0,
                                                                            forwardCurveBuilder.Build(), currentPeriod);
            return valuationResults;
        }

        //[Fact]
        //public void Calculate_ZeroInventoryForwardSpreadHigherThanCycleCost_ResultWithNetPresentValueSpreadMinusCycleCostTimesVolume()
        //{
        //    // Cycle cost = 2.0, so create curve with spread of 2.01
        //    var valuationResults = IntrinsicValuationZeroInventoryForwardCurveWithSpread(200000.01);


        //    Assert.True(valuationResults.NetPresentValue > 0);
        //}

        [Fact]
        public void Calculate_CurrentPeriodAfterStorageEnd_ResultWithZeroNetPresentValue()
        {
            var currentPeriod = new Day(2019, 10, 1);

            IntrinsicStorageValuationResults<Day> valuationResults = GenerateValuationResults(0.0,
                                                                        TimeSeries<Day, double>.Empty, currentPeriod);

            Assert.Equal(0.0, valuationResults.NetPresentValue);
        }

        [Fact]
        public void Calculate_CurrentPeriodAfterStorageEnd_ResultWithEmptyDecisionProfile()
        {
            var currentPeriod = new Day(2019, 10, 1);

            IntrinsicStorageValuationResults<Day> valuationResults = GenerateValuationResults(0.0,
                                                                        TimeSeries<Day, double>.Empty, currentPeriod);

            Assert.True(valuationResults.DecisionProfile.IsEmpty);
        }


        // TODO test cases:
        // Empty + spread more than inject + withdraw cost = value is spread minus costs, profile has inject withdraw
        // Inventory + curve backwardated: value is highest part of curve * volume - withdraw cost, profile is in highest part of curve
        // Expired: zero value and zero length profile
        // Inventory + spread less than inject + withdraw cost + terminal value = terminal value and zero profile (discounted?)
        // Single period with high forward price: npv and profile all in this part
        // Decision profile * forward curve discounted = NPV
        // Multiple cycles
        // Inject and withdraw rates equal (or multiples), net profile will be zero OR

    }
}
