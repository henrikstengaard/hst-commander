import React from 'react';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Paper from '@mui/material/Paper';

export default function EntryList({ id, entries, cursorIndex = -1 }) {
    return (
        <TableContainer component={Paper}>
            <Table id={id} stickyHeader size="small">
                <TableHead>
                    <TableRow>
                        <TableCell>Name</TableCell>
                        <TableCell align="right">Size</TableCell>
                        <TableCell align="right">Date</TableCell>
                    </TableRow>
                </TableHead>
                <TableBody>
                    {entries.map((entry, index) => (
                        <TableRow
                            key={entry.id}
                            sx={{ '&:last-child td, &:last-child th': { border: 0 } }}
                            selected={cursorIndex === index}
                        >
                            <TableCell component="th" scope="row">
                                {entry.name}
                            </TableCell>
                            <TableCell align="right">{entry.size}</TableCell>
                            <TableCell align="right">{entry.date}</TableCell>
                        </TableRow>
                    ))}
                </TableBody>
            </Table>
        </TableContainer>
    )
}