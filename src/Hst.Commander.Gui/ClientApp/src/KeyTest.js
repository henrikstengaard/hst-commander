import React, { useEffect, useState, useReducer } from 'react';
import KeyPressHandler from "./keyPressHandler";
import EntryList from "./EntryList";
import Grid from '@mui/material/Grid';

const initialState = {
    activeList: 'left',
    left: {
        cursorIndex: 1
    },
    right: {
        cursorIndex: 1
    }
}

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

let state = {...initialState}

export default function KeyTest() {
    //const [, forceUpdate] = useReducer((x) => x + 1, 0)

    const keyPressed = KeyPressHandler();

    useEffect(() => {
        let cursorIndex = state[state.activeList].cursorIndex;
        switch (keyPressed.key) {
            case 'Tab':
                state.activeList = state.activeList === 'left' ? 'right' : 'left';
                break;
            case 'ArrowDown':
                cursorIndex++;
                if (cursorIndex >= entries.length)
                {
                    cursorIndex = entries.length - 1;
                }
                break;
            case 'ArrowUp':
                cursorIndex--;
                if (cursorIndex < 0)
                {
                    cursorIndex = 0;
                }
                break;
        }
        state[state.activeList].cursorIndex = cursorIndex;
    }, [keyPressed]);    
    
    return (
        <Grid container spacing={1}>
            <Grid item xs={6}>
                <EntryList entries={entries} cursorIndex={state.activeList === 'left' ? state['left'].cursorIndex : -1} />
            </Grid>
            <Grid item xs={6}>
                {/*<EntryList entries={entries} cursorIndex={state.activeList === 'right' ? state['right'].cursorIndex : -1} />*/}
            </Grid>
        </Grid>
    )
}
