import React, {useState} from 'react';
import logo from './logo.svg';
import './App.css';
import List2 from "./List2";
import useOverFlowHidden from "./useOverFlowHidden";
import KeyTest from "./KeyTest";
import Main from "./EmitterTest/Main";
import PlainList from "./PlainList";

function App() {
  //useOverFlowHidden();

  return (
    <div className="App">
      <header className="App-header">
        {/*<img src={logo} className="App-logo" alt="logo" />*/}
        {/*<p>*/}
        {/*  Edit <code>src/App.tsx</code> and save to reload.*/}
        {/*</p>*/}
        {/*<a*/}
        {/*  className="App-link"*/}
        {/*  href="https://reactjs.org"*/}
        {/*  target="_blank"*/}
        {/*  rel="noopener noreferrer"*/}
        {/*>*/}
        {/*  Learn React*/}
        {/*</a>*/}
        {/*<KeyTest/>*/}
        {/*<List2/>*/}
        {/*  <Main/>*/}
          <PlainList />
      </header>
    </div>
  );
}

export default App;
