import pandas as pd
import ipywidgets as ipw
import ipysheet as ips
from cmdty_storage import CmdtyStorage, three_factor_seasonal_value, intrinsic_value
from datetime import date, timedelta
from IPython.display import display
from ipywidgets.widgets.interaction import show_inline_matplotlib_plots
from collections import namedtuple


    # Shared properties
freq='D'
num_fwd_rows = 15
date_format = 'YYYY-MM-DD'
num_ratch_rows = 20
RatchetRow = namedtuple('RatchetRow', ['date', 'inventory', 'inject_rate', 'withdraw_rate'])

def enumerate_ratchets():
    ratchet_row = 0
    while ratchet_row < num_ratch_rows and ratch_input_sheet[ratchet_row, 1].value != '':
        yield RatchetRow(ratch_input_sheet[ratchet_row, 0].value, ratch_input_sheet[ratchet_row, 1].value,
                        ratch_input_sheet[ratchet_row, 3].value, ratch_input_sheet[ratchet_row, 2].value)
        ratchet_row+=1

def read_ratchets():
    ratchets = []
    for ratch in enumerate_ratchets():
        if ratch.date != '':
            dt_item = (pd.Period(ratch.date, freq=freq), [(ratch.inventory, -ratch.inject_rate,
                                                        ratch.withdraw_rate)])
            ratchets.append(dt_item)
        else:
            dt_item[1].append((ratch.inventory, -ratch.inject_rate,
                                                        ratch.withdraw_rate))
    return ratchets

# Forward curve input
fwd_input_sheet = ips.sheet(rows=num_fwd_rows, columns=2, column_headers=['fwd_start', 'price'])
for row_num in range(0, num_fwd_rows):
    ips.cell(row_num, 0, '', date_format=date_format, type='date')
    ips.cell(row_num, 1, '', type='numeric')

def on_stor_type_change(change):
    print(change)

# Common storage properties
stor_type_wgt = ipw.RadioButtons(options=['Simple', 'Ratchets'], description='Storage Type')
start_wgt = ipw.DatePicker(description='Start')
end_wgt = ipw.DatePicker(description='End')
inj_cost_wgt = ipw.FloatText(description='Injection Cost')
with_cost_wgt = ipw.FloatText(description='Withdrw Cost')
storage_common_wgt = ipw.HBox([ipw.VBox([start_wgt, end_wgt, inj_cost_wgt, with_cost_wgt]), stor_type_wgt])

# Simple storage type properties
invent_min_wgt = ipw.FloatText(description='Min Inventory')
invent_max_wgt = ipw.FloatText(description='Max Inventory')
inj_rate_wgt = ipw.FloatText(description='Injection Rate')
with_rate_wgt = ipw.FloatText(description='Withdrw Rate')
storage_simple_wgt = ipw.VBox([invent_min_wgt, invent_max_wgt, inj_rate_wgt, with_rate_wgt])

# Ratchet storage type properties

ratch_input_sheet = ips.sheet(rows=num_ratch_rows, columns=4, 
                              column_headers=['date', 'inventory', 'inject_rate', 'withdraw_rate'])
for row_num in range(0, num_ratch_rows):
    ips.cell(row_num, 0, '', date_format=date_format, type='date')
    ips.cell(row_num, 1, '', type='numeric')
    ips.cell(row_num, 2, '', type='numeric')
    ips.cell(row_num, 3, '', type='numeric')

# Compose storage
storage_details_wgt = ipw.VBox([storage_common_wgt, storage_simple_wgt])

def on_test_rad_change(change):
    if change['new'] == 'Simple':
        storage_details_wgt.children = (storage_common_wgt, storage_simple_wgt)
    else:
        storage_details_wgt.children = (storage_common_wgt, ratch_input_sheet)
stor_type_wgt.observe(on_test_rad_change, names='value')

val_date_wgt = ipw.DatePicker(description='Val Date', value=date.today())
inventory_wgt = ipw.FloatText(description='Inventory')

val_inputs_wgt = ipw.VBox([val_date_wgt, inventory_wgt])

ir_wgt = ipw.FloatText(description='Intrst Rate %', step=0.005)

spot_vol_wgt = ipw.FloatText(description='Spot Vol', step=0.01)
spot_mr_wgt = ipw.FloatText(description='Spot Mean Rev', step=0.01)
lt_vol_wgt = ipw.FloatText(description='Long Term Vol', step=0.01)
seas_vol_wgt = ipw.FloatText(description='Seasonal Vol', step=0.01)
vol_params_wgt = ipw.VBox([spot_vol_wgt, spot_mr_wgt, lt_vol_wgt, seas_vol_wgt])

# Technical Parameters
num_sims_wgt = ipw.IntText(description='Num Sims', value=1000, step=500)
seed_is_random_wgt = ipw.Checkbox(description='Seed is Random', value=False)
random_seed_wgt = ipw.IntText(description='Seed', value=11)
grid_points_wgt = ipw.IntText(description='Grid Points', value=100, step=10)
basis_funcs_label_wgt = ipw.Label('Basis Functions')
basis_funcs_legend_wgt = ipw.VBox([ipw.Label('1=Constant'),
                                    ipw.Label('s=Spot Price'),
                                    ipw.Label('x_st=Short-term Factor'),
                                   ipw.Label('x_sw=Sum/Win Factor'),
                                   ipw.Label('x_lt=Long-term Factor')])

basis_funcs_input_wgt = ipw.Textarea(
    value='1 + x_st + x_sw + x_lt + x_st**2 + x_sw**2 + x_lt**2 + x_st**3 + x_sw**3 + x_lt**3',
    layout=ipw.Layout(width='95%', height='95%'))
basis_func_wgt = ipw.HBox([ipw.VBox([basis_funcs_label_wgt, basis_funcs_legend_wgt]), basis_funcs_input_wgt])
num_tol_wgt = ipw.FloatText(description='Numerical Tol', value=1E-10, step=1E-9)

def on_seed_is_random_change(change):
    if change['new']:
        random_seed_wgt.disabled = True
    else:
        random_seed_wgt.disabled = False

seed_is_random_wgt.observe(on_seed_is_random_change, names='value')

tech_params_wgt = ipw.HBox([ipw.VBox([num_sims_wgt, seed_is_random_wgt, random_seed_wgt, grid_points_wgt, 
                            num_tol_wgt]), basis_func_wgt])

# Output Widgets
progress_wgt = ipw.FloatProgress(min=0.0, max=1.0)
full_value_wgt = ipw.Text(description='Full Value', disabled=True)
intr_value_wgt = ipw.Text(description='Intr. Value', disabled=True)
extr_value_wgt = ipw.Text(description='Extr. Value', disabled=True)
value_wgts = [full_value_wgt, intr_value_wgt, extr_value_wgt]
values_wgt = ipw.VBox(value_wgts)

out = ipw.Output()

mkt_data_wgt = ipw.HBox([val_inputs_wgt, fwd_input_sheet, ipw.VBox([vol_params_wgt, ir_wgt])])

tab = ipw.Tab()
tab_titles = ['Valuation Data', 'Storage Details', 'Technical Params']
for idx, title in enumerate(tab_titles):
    tab.set_title(idx, title)
tab.children = [mkt_data_wgt, storage_details_wgt, tech_params_wgt]

def on_progress(progress):
    progress_wgt.value = progress

# Inputs Not Defined in GUI
def twentieth_of_next_month(period): return period.asfreq('M').asfreq('D', 'end') + 20

def read_fwd_curve():
    fwd_periods = []
    fwd_prices = []
    fwd_row=0
    while fwd_input_sheet[fwd_row, 0].value != '':
        fwd_periods.append(pd.Period(fwd_input_sheet[fwd_row, 0].value, freq=freq))
        fwd_prices.append(fwd_input_sheet[fwd_row, 1].value)
        fwd_row+=1
    return pd.Series(fwd_prices, pd.PeriodIndex(fwd_periods)).resample(freq).fillna('pad')

def btn_clicked(b):
    progress_wgt.value = 0.0
    for vw in value_wgts:
        vw.value = ''
    btn.disabled = True
    out.clear_output()
    try:
        global fwd_curve
        fwd_curve = read_fwd_curve()
        global storage
        global val_results_3f
        if stor_type_wgt.value == 'Simple':
            storage = CmdtyStorage(freq, storage_start=start_wgt.value, storage_end=end_wgt.value, 
                                   injection_cost=inj_cost_wgt.value, withdrawal_cost=with_cost_wgt.value,
                                  min_inventory=invent_min_wgt.value, max_inventory=invent_max_wgt.value,
                                  max_injection_rate=inj_rate_wgt.value, max_withdrawal_rate=with_rate_wgt.value)
        else:
            ratchets = read_ratchets()
            storage = CmdtyStorage(freq, storage_start=start_wgt.value, storage_end=end_wgt.value, 
                       injection_cost=inj_cost_wgt.value, withdrawal_cost=with_cost_wgt.value,
                       constraints=ratchets)

        interest_rate_curve = pd.Series(index=pd.period_range(val_date_wgt.value, 
                                  twentieth_of_next_month(pd.Period(end_wgt.value, freq='D')), freq='D'), dtype='float64')
        interest_rate_curve[:] = ir_wgt.value
        seed = None if seed_is_random_wgt.value else random_seed_wgt.value
        val_results_3f = three_factor_seasonal_value(storage, val_date_wgt.value, inventory_wgt.value, fwd_curve=fwd_curve,
                                     interest_rates=interest_rate_curve, settlement_rule=twentieth_of_next_month,
                                    spot_mean_reversion=spot_mr_wgt.value, spot_vol=spot_vol_wgt.value,
                                    long_term_vol=lt_vol_wgt.value, seasonal_vol=seas_vol_wgt.value,
                                    num_sims=num_sims_wgt.value, 
                                    basis_funcs=basis_funcs_input_wgt.value, seed=seed,
                                    num_inventory_grid_points=grid_points_wgt.value, on_progress_update=on_progress,
                                    numerical_tolerance=num_tol_wgt.value)
        full_value_wgt.value = "{0:,.0f}".format(val_results_3f.npv)
        intr_value_wgt.value = "{0:,.0f}".format(val_results_3f.intrinsic_npv)
        extr_value_wgt.value = "{0:,.0f}".format(val_results_3f.extrinsic_npv)
        intr_delta = val_results_3f.intrinsic_profile['net_volume']
        with out:
            ax_1 = val_results_3f.deltas.plot(legend=True)
            ax_1.set_ylabel('Delta')
            intr_delta.plot(legend=True, ax=ax_1)
            active_fwd_curve = fwd_curve[storage.start:storage.end]
            ax_2 = active_fwd_curve.plot(secondary_y=True, legend=True, ax=ax_1)
            ax_2.set_ylabel('Forward Price')
            ax_1.legend(['Full Delta', 'Intrinsic Delta'])
            ax_2.legend(['Forward Curve'])
            show_inline_matplotlib_plots()
    except Exception as e:
        with out:
            print('Exception:')
            print(e)
    finally:
        btn.disabled = False


btn = ipw.Button(description='Calculate')
btn.on_click(btn_clicked)  

def display_gui():
    display(tab)
    display(btn)
    display(progress_wgt)
    display(values_wgt)
    display(out)

def test_data_btn():
    def btn_clicked_2(b):
        today = date.today()
        inventory_wgt.value = 1456
        start_wgt.value = today + timedelta(days=5)
        end_wgt.value = today + timedelta(days=380)
        invent_max_wgt.value = 100000
        inj_rate_wgt.value = 260
        with_rate_wgt.value = 130
        inj_cost_wgt.value = 1.1
        with_cost_wgt.value = 1.3
        ir_wgt.value = 0.005
        spot_vol_wgt.value = 1.23
        spot_mr_wgt.value = 14.5
        lt_vol_wgt.value = 0.23
        seas_vol_wgt.value = 0.39
        for idx, price in enumerate([58.89, 61.41, 62.58, 58.9, 43.7, 58.65, 61.45, 56.87]):
            fwd_input_sheet[idx, 1].value = price
        for idx, do in enumerate([0, 30, 60, 90, 150, 250, 350, 400]):
            fwd_input_sheet[idx, 0].value = (today + timedelta(days=do)).strftime('%Y-%m-%d')
        # Populate ratchets
        ratch_input_sheet[0, 0].value = today.strftime('%Y-%m-%d')
        for idx, inv in enumerate([0.0, 25000.0, 50000.0, 60000.0, 65000.0]):
            ratch_input_sheet[idx, 1].value = inv
        for idx, inj in enumerate([650.0, 552.5, 512.8, 498.6, 480.0]):
            ratch_input_sheet[idx, 2].value = inj
        for idx, wthd in enumerate([702.7, 785.0, 790.6, 825.6, 850.4]):
            ratch_input_sheet[idx, 3].value = wthd
        ratch_2_offset = 5
        ratch_input_sheet[ratch_2_offset, 0].value = (today + timedelta(days = 150)).strftime('%Y-%m-%d')
        for idx, inv in enumerate([0.0, 24000.0, 48000.0, 61000.0, 65000.0]):
            ratch_input_sheet[ratch_2_offset + idx, 1].value = inv
        for idx, inj in enumerate([645.8, 593.65, 568.55, 560.8, 550.0]):
            ratch_input_sheet[ratch_2_offset + idx, 2].value = inj
        for idx, wthd in enumerate([752.5, 813.7, 836.45, 854.78, 872.9]):
            ratch_input_sheet[ratch_2_offset + idx, 3].value = wthd

    btn2 = ipw.Button(description='Populate Test Data')
    btn2.on_click(btn_clicked_2)

    display(btn2)