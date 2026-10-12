from slice_job import technological_grid, microns

assert technological_grid(20,0.8)==[n*800000/1000000 for n in range(25)]
assert technological_grid(20.01,0.8)[-1]==19.2
assert technological_grid(20.8,0.8)[-1]==20
assert technological_grid(0.7,0.8)==[]
assert technological_grid(0.8,0.8)==[0]
assert microns(0.8)==800000
for zmax,step in ((-1,0.8),(20,0),(20,-0.1),(20,0.000001),(20,float('nan'))):
    try: technological_grid(zmax,step)
    except (ValueError,ArithmeticError): pass
    else: raise AssertionError((zmax,step))
print('PASS: exact technological grid, zero first plane, lower-grid upper limit, short-segment and invalid-input cases.')
