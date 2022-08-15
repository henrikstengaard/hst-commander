import React, { useState, useEffect, Dispatch, SetStateAction, createRef, RefObject } from "react";

const useKeyPress = function (targetKey: string, ref: RefObject<HTMLInputElement>) {
    const [keyPressed, setKeyPressed] = useState(false);


    function downHandler({ key }: { key: string }) {
        if (key === targetKey) {
            setKeyPressed(true);
        }
    }

    const upHandler = ({ key }: { key: string }) => {
        if (key === targetKey) {
            setKeyPressed(false);
        }
    };

    React.useEffect(() => {
        const currentRef = ref.current;
        if (!currentRef) {
            return;
        }
        
        currentRef.addEventListener("keydown", downHandler);
        currentRef.addEventListener("keyup", upHandler);

        return () => {
            currentRef.removeEventListener("keydown", downHandler);
            currentRef.removeEventListener("keyup", upHandler);
        };
    });

    return keyPressed;
};

const items = [
    { id: 1, name: "Josh Weir" },
    { id: 2, name: "Sarah Weir" },
    { id: 3, name: "Alicia Weir" },
    { id: 4, name: "Doo Weir" },
    { id: 5, name: "Grooft Weir" }
];

type itemType = { id: number, name: string }

type ListItemType = {
    item: itemType
    , active: boolean
    , setSelected: Dispatch<SetStateAction<SetStateAction<itemType | undefined>>>
    , setHovered: Dispatch<SetStateAction<itemType | undefined>>
}

const ListItem = ({ item, active, setSelected, setHovered }: ListItemType) => (
    <div
        className={`item ${active ? "active" : ""}`}
        onClick={() => setSelected(item)}
        onMouseEnter={() => setHovered(item)}
        onMouseLeave={() => setHovered(undefined)}
    >
        {item.name}
    </div>
);

export default function List() {
    const searchBox = createRef<HTMLInputElement>()
    const [selected, setSelected] = useState<React.SetStateAction<itemType | undefined>>(undefined);
    const downPress = useKeyPress("ArrowDown", searchBox);
    const upPress = useKeyPress("ArrowUp", searchBox);
    const enterPress = useKeyPress("Enter", searchBox);
    const [cursor, setCursor] = useState<number>(0);
    const [hovered, setHovered] = useState<itemType | undefined>(undefined);
    const [searchItem, setSearchItem] = useState<string>("")

    // console.log('cursor', cursor);
    //const i = items[0]

    const handelChange = (e: React.SyntheticEvent<HTMLInputElement>) => {
        setSelected(undefined)
        setSearchItem(e.currentTarget.value)
    }

    useEffect(() => {
        if (items.length && downPress) {
            setCursor(prevState =>
                prevState < items.length - 1 ? prevState + 1 : prevState
            );
        }
    }, [downPress]);
    useEffect(() => {
        if (items.length && upPress) {
            setCursor(prevState => (prevState > 0 ? prevState - 1 : prevState));
        }
    }, [upPress]);
    useEffect(() => {
        if ((items.length && enterPress) || (items.length && hovered)) {
            setSelected(items[cursor]);
        }
    }, [cursor, enterPress, hovered]);
    useEffect(() => {
        if (items.length && hovered) {
            setCursor(items.indexOf(hovered));
        }
    }, [hovered]);

    return (
        <div>
            <p>
                <small>
                    Use up down keys and hit enter to select, or use the mouse
                </small>
            </p>
            <div>
                <input ref={searchBox} type="text" onChange={handelChange} value={selected ? selected.name : searchItem} />
                {items.map((item, i) => (
                    <ListItem

                        key={item.id}
                        active={i === cursor}
                        item={item}
                        setSelected={setSelected}
                        setHovered={setHovered}
                    />
                ))}
            </div>
        </div>
    );
}