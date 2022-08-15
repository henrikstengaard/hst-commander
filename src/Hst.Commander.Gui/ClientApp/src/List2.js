import { useEffect, useReducer } from "react";
import useKeyPress from "./useKeyPress";

const initialState = { selectedIndex: 0 };
const list = ["🍎 apple", "🍊 orange", "🍍 pineapple", "🍌 banana"];

function reducer(state, action) {
    switch (action.type) {
        case "arrowUp":
            return {
                selectedIndex:
                    state.selectedIndex !== 0 ? state.selectedIndex - 1 : list.length - 1
            };
        case "arrowDown":
            return {
                selectedIndex:
                    state.selectedIndex !== list.length - 1 ? state.selectedIndex + 1 : 0
            };
        case "select":
            return { selectedIndex: action.payload };
        default:
            throw new Error();
    }
}

const List2 = () => {
//    const arrowUpPressed = useKeyPress("ArrowUp");
    const arrowDownPressed = useKeyPress("ArrowDown");
    const [state, dispatch] = useReducer(reducer, initialState);

    // useEffect(() => {
    //     if (arrowUpPressed) {
    //         dispatch({ type: "arrowUp" });
    //     }
    // }, [arrowUpPressed]);

    useEffect(() => {
        if (arrowDownPressed === "ArrowDown") {
            dispatch({ type: "arrowDown" });
        }
    }, [arrowDownPressed]);

    return (
        <div>
            {list.map((item, i) => (
                <div
                    key={item}
                    onClick={() => {
                        dispatch({ type: "select", payload: i });
                    }}
                    style={{
                        cursor: "pointer",
                        color: i === state.selectedIndex ? "red" : "black"
                    }}
                    role="button"
                    aria-pressed={i === state.selectedIndex}
                    tabIndex={0}
                    onKeyPress={(e) => {
                        if (e.key === "Enter") {
                            dispatch({ type: "select", payload: i });
                            e.target.blur();
                        }
                    }}
                >
                    {item}
                </div>
            ))}
        </div>
    );
};

export default List2;