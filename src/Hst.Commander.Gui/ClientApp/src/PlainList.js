import React, {useEffect} from 'react';
import EntryList from "./EntryList";
import Grid from '@mui/material/Grid';

const entries = [{
    id: 1,
    name: 'Program Files',
    size: 0,
    date: ''
},{
    id: 2,
    name: 'config.sys',
    size: 0,
    date: ''
},{
    id: 3,
    name: 'pagefile.sys',
    size: 0,
    date: ''
},{
    id: 4,
    name: 'Documents',
    size: 0,
    date: ''
},{
    id: 5,
    name: 'Users',
    size: 0,
    date: ''
},{
    id: 6,
    name: 'Users',
    size: 0,
    date: ''
},{
    id: 7,
    name: 'Users',
    size: 0,
    date: ''
},{
    id: 8,
    name: 'Users',
    size: 0,
    date: ''
},{
    id: 9,
    name: 'Users',
    size: 0,
    date: ''
},{
    id: 10,
    name: 'Users',
    size: 0,
    date: ''
}]

export default function PlainList() {
    useEffect(() => {
        window.updateNavigationState();
    }, [])
    
    return (
        <Grid container spacing={1}>
            <Grid item xs={6}>
                <EntryList id={"navigation-left"} entries={entries} />
            </Grid>
            <Grid item xs={6}>
                <EntryList id={"navigation-right"} entries={entries} />
            </Grid>
        </Grid>
    )
}
