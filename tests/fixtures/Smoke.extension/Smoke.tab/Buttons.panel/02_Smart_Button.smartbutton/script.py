"""Smartbutton contract demo: runs once at ribbon build with __selfinit__ True
and a __button__ proxy, then normally on every click."""
if __selfinit__:
    __button__.Title = 'Smart (init OK)'
    __button__.Tooltip = 'This title was set by __selfinit__ at ribbon build.'
else:
    from pynavis import toast
    toast.info('Smartbutton clicked (selfinit renamed me at build time)')
