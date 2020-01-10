# Copyright(c) 2019 Jake Fowler
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use, 
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.

import unittest
from cmdty_storage import CmdtyStorage, InjectWithdrawByInventoryAndPeriod, InjectWithdrawByInventory
from datetime import date
import pandas as pd

class TestCmdtyStorage(unittest.TestCase):

    def test_initializer(self):

        constraints =   [
                            InjectWithdrawByInventoryAndPeriod(date(2019, 8, 28), 
                                        [
                                            InjectWithdrawByInventory(0.0, -150.0, 255.2),
                                            InjectWithdrawByInventory(2000.0, -200.0, 175.0),
                                        ]),
                    # TODO specify constraint with tuples
                ]

        constant_injection_cost = 0.015
        constant_pcnt_consumed_inject = 0.0001
        constant_withdrawal_cost = 0.02
        constant_pcnt_consumed_withdraw = 0.000088

        storage = CmdtyStorage('D', date(2019, 8, 28), date(2019, 9, 25), constraints, constant_injection_cost,
                                constant_withdrawal_cost, constant_pcnt_consumed_inject, constant_pcnt_consumed_withdraw)
        
        self.assertEqual(True, storage.must_be_empty_at_end)

        self.assertEqual('D', storage.freq)

        self.assertEqual(pd.Period(date(2019, 8, 28), freq='D'), storage.start_period)

        self.assertEqual(pd.Period(date(2019, 9, 25), freq='D'), storage.end_period)

        # Inventory half way between pillars, so assert against mean of min/max inject/withdraw at the pillars
        min_dec, max_dec = storage.inject_withdraw_range(date(2019, 8, 29), 1000.0)
        self.assertEqual(-175.0, min_dec)
        self.assertEqual((255.2 + 175.0)/2.0, max_dec)


if __name__ == '__main__':
    unittest.main()
