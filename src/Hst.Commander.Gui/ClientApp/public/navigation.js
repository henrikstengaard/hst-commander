window.initNavigationState = () => {
    if (window.navigationState === null) {
        return;
    }

    window.navigationState = {
        activeTab: 'left',
        tabs: {
            left: {
                elements: [],
                cursorIndex: 0
            },
            right: {
                elements: [],
                cursorIndex: 0
            }
        }
    };
}

window.updateNavigationState = () => {
    window.initNavigationState();
    window.navigationState.tabs.left.cursorIndex = 0;
    window.navigationState.tabs.left.elements = document.querySelectorAll("#navigation-left tbody tr")
    window.navigationState.tabs.right.cursorIndex = 0;
    window.navigationState.tabs.right.elements = document.querySelectorAll("#navigation-right tbody tr")

    const currentTab = window.navigationState.tabs[window.navigationState.activeTab];

    if (currentTab.elements.length === 0) {
        return;
    }

    currentTab.elements[currentTab.cursorIndex].classList.add('Mui-selected');
}

function keydownHandler(e) {
    const activeTab = window.navigationState.activeTab;
    let currentTab = window.navigationState.tabs[activeTab];

    if (currentTab.elements.length === 0) {
        return;
    }

    let cursorIndex = currentTab.cursorIndex;
    const prevCursorIndex = cursorIndex;

    switch(e.key) {
        case 'Tab':
            currentTab.elements[cursorIndex].classList.remove('Mui-selected');

            // switch active tab
            window.navigationState.activeTab = activeTab === 'left' ? 'right' : 'left';

            currentTab = window.navigationState.tabs[window.navigationState.activeTab];
            currentTab.elements[currentTab.cursorIndex].classList.add('Mui-selected');
            break;
        case 'ArrowDown':
            if (cursorIndex < currentTab.elements.length - 1) {
                cursorIndex++;
            }
            break;
        case 'ArrowUp':
            if (cursorIndex > 0) {
                cursorIndex--;
            }
            break;
    }

    if (currentTab.elements.length > 0 && prevCursorIndex !== cursorIndex) {
        currentTab.elements[prevCursorIndex].classList.remove('Mui-selected');
        currentTab.elements[cursorIndex].classList.add('Mui-selected');
        currentTab.cursorIndex = cursorIndex;
    }

    e.preventDefault();
}

window.addEventListener("keydown", keydownHandler, false);