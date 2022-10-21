"use strict";

var blazorContextMenu = function (blazorContextMenu) {

    var closest = null;
    if (window.Element && !Element.prototype.closest) {
        closest = function (el, s) {
            var matches = (el.document || el.ownerDocument).querySelectorAll(s), i;
            do {
                i = matches.length;
                while (--i >= 0 && matches.item(i) !== el) { };
            } while ((i < 0) && (el = el.parentElement));
            return el;
        };
    }
    else {
        closest = function (el, s) {
            return el.closest(s);
        };
    }


    var openMenus = [];

    //Helper functions
    //========================================
    function guid() {
        function s4() {
            return Math.floor((1 + Math.random()) * 0x10000)
                .toString(16)
                .substring(1);
        }
        return s4() + s4() + '-' + s4() + '-' + s4() + '-' + s4() + '-' + s4() + s4() + s4();
    }

    function findFirstChildByClass(element, className) {
        var foundElement = null;
        function recurse(element, className, found) {
            for (var i = 0; i < element.children.length && !found; i++) {
                var el = element.children[i];
                if (el.classList.contains(className)) {
                    found = true;
                    foundElement = element.children[i];
                    break;
                }
                if (found)
                    break;
                recurse(element.children[i], className, found);
            }
        }
        recurse(element, className, false);
        return foundElement;
    }

    function findAllChildsByClass(element, className) {
        var foundElements = new Array();
        function recurse(element, className) {
            for (var i = 0; i < element.children.length; i++) {
                var el = element.children[i];
                if (el.classList.contains(className)) {
                    foundElements.push(element.children[i]);
                }
                recurse(element.children[i], className);
            }
        }
        recurse(element, className);
        return foundElements;
    }

    function removeItemFromArray(array, item) {
        for (var i = 0; i < array.length; i++) {
            if (array[i] === item) {
                array.splice(i, 1);
            }
        }
    }


    //===========================================

    var menuHandlerReference = null;
    //var openingMenu = false;

    blazorContextMenu.SetMenuHandlerReference = function (dotnetRef) {
        if (!menuHandlerReference) {
            menuHandlerReference = dotnetRef;
        }
    }

    var addToOpenMenus = function (menu, menuId, target, toggle = undefined) {
        var instanceId = guid();
        openMenus.push({
            id: menuId,
            target: target,
            toggle: toggle,
            instanceId: instanceId
        });
        menu.dataset["instanceId"] = instanceId;
    };

    blazorContextMenu.ManualShow = function (menuId, x, y) {
        //openingMenu = true;
        var menu = document.getElementById(menuId);
        if (!menu) throw new Error("No context menu with id '" + menuId + "' was found");
        addToOpenMenus(menu, menuId, null);
        showMenuCommon(menu, menuId, x, y, null, null);
    }

    blazorContextMenu.OnContextMenu = function (e, menuId, stopPropagation) {
        //openingMenu = true;
        var menu = document.getElementById(menuId);
        if (!menu) throw new Error("No context menu with id '" + menuId + "' was found");
        addToOpenMenus(menu, menuId, e.target);
        const triggerDotnetRef = getTriggerDotnetRef(e.currentTarget);
        showMenuCommon(menu, menuId, e.x, e.y, e, triggerDotnetRef);
        e.preventDefault();
        if (stopPropagation) {
            e.stopPropagation();
        }
        return false;
    };

    let hoverMenuReference = null;
    let hoverMenuTarget = null;
    let hideHoverMenuOnLeave = true;
    let debug = false;

    blazorContextMenu.SetHideHoverMenuOnLeave = function(value) {
        // It's hard to inspect hover menu elements.
        // Setting this option to false, simplifies this task.
        // When option is false, hover menu is not hiding on leaving target element.
        // Only moving over other element hides current menu.
        hideHoverMenuOnLeave = value;
    }

    blazorContextMenu.OnHoverContextMenu = function (e, menuId, stopPropagation) {
        if (debug) console.log('OnHoverContextMenu invoked for ', e.currentTarget);
        innerOnHoverContextMenu(e, menuId);
        e.preventDefault();
        if (stopPropagation)
            e.stopPropagation();
        return false;
    }

    const innerOnHoverContextMenu = function(e, menuId) {
        if (hoverMenuTarget) {
            if (hoverMenuTarget === e.currentTarget) {
                if (debug) console.log('OnHoverContextMenu re-entered to current target ', hoverMenuTarget);
                hoverMenuTarget.removeEventListener('mouseout', onMouseOutHoverMenu);
                hoverMenuTarget.addEventListener('mouseleave', onMouseLeftHoverMenu);
                return;
            }
            else {
                if (debug) console.log('OnHoverContextMenu stopped in target ', hoverMenuTarget);
                closeHoverMenu();
            }
        }

        if (debug) console.log('OnHoverContextMenu started for ', e.currentTarget);
        const menu = document.getElementById(menuId);
        if (!menu) throw new Error("No context menu with id '" + menuId + "' was found");
        const {x, y, isVisible} = getPlacement(e);
        if (debug) console.log('hover menu placement is (x,y, isVisible):', x, y, isVisible);
        if (!isVisible) {
            // Do not show menu if placement is not visible
            if (debug) console.log('hover menu placement is not visible');
            closeHoverMenu();
            return;
        }

        hoverMenuTarget = e.currentTarget;
        hoverMenuReference = menu;

        hoverMenuTarget.addEventListener('mouseleave', onMouseLeftHoverMenu);
        addToOpenMenus(menu, menuId, e.target);
        const triggerDotnetRef = getTriggerDotnetRef(e.currentTarget);

        showMenuCommon(menu, menuId, x, y, e, triggerDotnetRef);
    }

    const onMouseLeftHoverMenu = function (e) {
        if (debug) console.log('onmouseleave invoked for ', e.currentTarget);
        e.currentTarget.removeEventListener('mouseleave', onMouseLeftHoverMenu);
        const isOverMenu = isOverHoverMenu(e);
        if (isOverMenu) {
            if (debug) console.log('mouse moved over hover menu for ', e.currentTarget);
            hoverMenuReference.addEventListener('mouseout', onMouseOutHoverMenu);
        }
        else {
            if (debug) console.log('mouse left from ', e.currentTarget);
            closeHoverMenu();
        }
    }

    const onMouseOutHoverMenu = function (e) {
        if (debug) console.log('mouseout invoked for ', e.currentTarget);
        const isOverMenu = isOverHoverMenu(e);
        if (isOverMenu) {
            if (debug) console.log('mouse is over hover menu for', hoverMenuTarget);
            return;
        }
        if (debug) console.log('mouse left hover menu for', hoverMenuTarget);
        if (hideHoverMenuOnLeave)
            closeHoverMenu();
    }

    const closeHoverMenu = function() {
        if (hoverMenuTarget) {
            hoverMenuTarget.removeEventListener('mouseleave', onMouseLeftHoverMenu);
            hoverMenuTarget.removeEventListener('mouseout', onMouseOutHoverMenu);
            hoverMenuTarget = null;
        }
        if (hoverMenuReference) {
            blazorContextMenu.Hide(hoverMenuReference.id);
            hoverMenuReference = null;
        }
    };

    const isOverHoverMenu = function (e) {
        if (!hoverMenuReference)
            return false;
        const elements = document.elementsFromPoint(e.pageX, e.pageY);
        for (const element of elements) {
            if (element === hoverMenuReference)
                return true;
        }
        return false;
    }

    const getTriggerDotnetRef = function (trigger) {
        const attrs = trigger.attributes;
        for(const attr of attrs) {
            const name = attr.name;
            const prefix = '_bl_';
            if (name.startsWith(prefix))
                return name.substring(prefix.length);
        }
        return "";
    }

    var getOpenedMenuForToggle = function (toggle) {
        if (openMenus.length > 0) {
            for (var i = 0; i < openMenus.length; i++) {
                var currentMenu = openMenus[i];
                if (currentMenu.toggle === toggle) {
                    return currentMenu;
                }
            }
        }
        return undefined;
    }

    blazorContextMenu.OnContextMenuToggle = function (e, menuId, stopPropagation) {
        //openingMenu = true;
        const menu = document.getElementById(menuId);
        if (!menu) throw new Error("No context menu with id '" + menuId + "' was found");
        const target = e.currentTarget;
        const currentMenu = getOpenedMenuForToggle(target);
        if (currentMenu) {
            blazorContextMenu.Hide(currentMenu.id);
            return false;
        }
        const {x, y} = getPlacement(e);
        addToOpenMenus(menu, menuId, e.target, target);
        const triggerDotnetRef = getTriggerDotnetRef(e.currentTarget);
        showMenuCommon(menu, menuId, x, y, e, triggerDotnetRef);
        e.preventDefault();
        if (stopPropagation) {
            e.stopPropagation();
        }
        return false;
    };

    let getPlacement = function (e) {
        const target = e.currentTarget;
        const placement = target.getElementsByClassName('placement');
        let x = e.x;
        let y = e.y;
        let isVisible = true;
        if (placement && placement.length > 0) {
            const placementEl = placement[0];
            const rect = placementEl.getBoundingClientRect();
            x = rect.left + (rect.width / 2.0);
            y = rect.top + (rect.height / 2.0);
            const elements = document.elementsFromPoint(x, y);
            let i = 0;
            for (const element of elements) {
                if (element === placementEl)
                    break;
                const parentElement = element.parentElement;
                const isContextMenu = parentElement.tagName === 'DIV' && parentElement.className === 'context-menu-container';
                if (isContextMenu)
                    continue;
                const style = window.getComputedStyle(element, null);
                const bgColor = style.getPropertyValue("background-color");
                const opacity = style.getPropertyValue("opacity");
                const isTransparent = bgColor === "rgba(0, 0, 0, 0)" || opacity === "0";
                if (!isTransparent) {
                    isVisible = false;
                    if (debug) console.log('not transparent element', element);
                    break;
                }
                i++;
            }
        }
        const menuPosition = target.dataset['menuPosition'];
        // Menu should be placed on left from the target point
        if (menuPosition === 'left')
            // We use negative X coordinate to indicate that menu should be positioned relative to the right edge.
            x = -(window.innerWidth - x);
        return {x: x, y: y, isVisible: isVisible};
    }

    let showMenuCommon = function (menu, menuId, x, y, event, triggerDotnetRef) {
        const target = event.target;
        const currentTarget = event.currentTarget;
        const offset = 5;
        const isLeftHalf = x < window.innerWidth / 2;
        const isTopHalf = y < window.innerHeight / 2;
        if (currentTarget.dataset.contextMenuToggle === undefined) {
            return blazorContextMenu.Show(menuId, x, y, target, triggerDotnetRef).then(function () {
                // When a menu is positioned relative to the right edge, correction is not needed.
                if (menu.style.left !== '') {
                    if (isLeftHalf)
                        menu.style.left = x + "px";
                    else
                        menu.style.left = (x - menu.clientWidth) + "px";
                }
                let topOverflownPixels = menu.offsetTop + menu.clientHeight - window.innerHeight;
                if (topOverflownPixels > 0) {
                    menu.style.top = (window.innerHeight - menu.clientHeight - offset) + "px";
                }
            });
        }
        // @Andrew: can you remind what do you try to achieve at this branch
        let btn = target.closest('button');
        let rect = btn.getBoundingClientRect();
        let left = rect.left;
        let right = rect.right;
        let top = rect.top;
        let bottom = rect.bottom;
        let menuLeft = right + offset;
        let menuTop = top;
        return blazorContextMenu.Show(menuId, x, y, target, triggerDotnetRef).then(function () {
            let menuWidth = menu.clientWidth;
            let menuHeight = menu.clientHeight;
            let menuBottom = menu.getBoundingClientRect().bottom;
            if (!isLeftHalf) {
                menuLeft = left - menuWidth - offset;
            }
            if (menuBottom > window.innerHeight) {
                menuTop = window.innerHeight - menuHeight - offset;
            }
            if (menu.classList.contains('dropdown-menu')) {
                menuLeft = left;
                menuTop = bottom + offset;
                if (!isTopHalf) {
                    menuTop = top - menuHeight - offset;
                }
                if (!isLeftHalf) {
                    menuLeft = right - menuWidth - offset;
                }
            }
            menu.style.left = menuLeft + "px";
            menu.style.top = menuTop + "px";
        });
    }

    blazorContextMenu.Init = function () {
        document.addEventListener("mouseup", function (e) {
            handleAutoHideEvent(e, "mouseup");
        });

        document.addEventListener("mousedown", function (e) {
            handleAutoHideEvent(e, "mousedown");
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === 'Escape' || e.key === 'Esc')
                handleAutoHideEvent(e, "escape");
        });

        function handleAutoHideEvent(e, autoHideEvent) {
            if (openMenus.length > 0) {
                for (var i = 0; i < openMenus.length; i++) {
                    var currentMenu = openMenus[i];
                    var menuElement = document.getElementById(currentMenu.id);
                    if (menuElement && menuElement.dataset["autohide"] == "true") {
                        if (autoHideEvent === 'escape')
                            blazorContextMenu.Hide(currentMenu.id);
                        else if (menuElement.dataset["autohideevent"] == autoHideEvent) {
                            var clickedInsideMenu = menuElement.contains(e.target);
                            var clickedInsideToggle = currentMenu.toggle && currentMenu.toggle.contains(e.target);
                            if (!(clickedInsideMenu || clickedInsideToggle)) {
                                blazorContextMenu.Hide(currentMenu.id);
                            }
                        }
                    }
                }
            }
        }

        window.addEventListener('resize', function () {
            if (openMenus.length > 0) {
                for (var i = 0; i < openMenus.length; i++) {
                    var currentMenu = openMenus[i];
                    var menuElement = document.getElementById(currentMenu.id);
                    if (menuElement && menuElement.dataset["autohide"] == "true") {
                        blazorContextMenu.Hide(currentMenu.id);
                    }
                }
            }
        }, true);
    };


    blazorContextMenu.Show = function (menuId, x, y, target, triggerDotnetRef) {
        if (menuHandlerReference == null)
            return new Promise((resolve, _) => resolve(undefined))

        var targetId = null;
        if (target) {
            if (!target.id) {
                //add an id to the target dynamically so that it can be referenced later
                //TODO: Rewrite this once this Blazor limitation is lifted
                target.id = guid();
            }
            targetId = target.id;
        }
        return menuHandlerReference.invokeMethodAsync('ShowMenu', menuId, x.toString(), y.toString(), targetId, triggerDotnetRef);
    }

    blazorContextMenu.Hide = function (menuId) {
        var menuElement = document.getElementById(menuId);
        var instanceId = menuElement.dataset["instanceId"];
        return menuHandlerReference.invokeMethodAsync('HideMenu', menuId).then(function (hideSuccessful) {
            if (menuElement.classList.contains("blazor-context-menu") && hideSuccessful) {
                //this is a root menu. Remove from openMenus list
                var openMenu = openMenus.find(function (item) {
                    return item.instanceId == instanceId;
                });
                if (openMenu) {
                    removeItemFromArray(openMenus, openMenu);
                }
            }
        });
    }

    var subMenuTimeout = null;
    blazorContextMenu.OnMenuItemMouseOver = function (e, xOffset, currentItemElement) {
        if (closest(e.target, ".blazor-context-menu__wrapper") != closest(currentItemElement, ".blazor-context-menu__wrapper")) {
            //skip child menu mouseovers
            return;
        }
        if (currentItemElement.getAttribute("itemEnabled") != "true") return;

        var subMenu = findFirstChildByClass(currentItemElement, "blazor-context-submenu");
        if (!subMenu) return; //item does not contain a submenu

        subMenuTimeout = setTimeout(function () {
            subMenuTimeout = null;

            var currentMenu = closest(currentItemElement, ".blazor-context-menu__wrapper");
            var currentMenuList = currentMenu.children[0];
            var rootMenu = closest(currentItemElement, ".blazor-context-menu");
            var targetRect = currentItemElement.getBoundingClientRect();
            var x = targetRect.left + currentMenu.clientWidth - xOffset;
            var y = targetRect.top;
            var instanceId = rootMenu.dataset["instanceId"];

            var openMenu = openMenus.find(function (item) {
                return item.instanceId == instanceId;
            });
            blazorContextMenu.Show(subMenu.id, x, y, openMenu.target).then(function () {
                var leftOverflownPixels = subMenu.offsetLeft + subMenu.clientWidth - window.innerWidth;
                if (leftOverflownPixels > 0) {
                    subMenu.style.left = (subMenu.offsetLeft - subMenu.clientWidth - currentMenu.clientWidth - xOffset) + "px"
                }

                var topOverflownPixels = subMenu.offsetTop + subMenu.clientHeight - window.innerHeight;
                if (topOverflownPixels > 0) {
                    subMenu.style.top = (subMenu.offsetTop - topOverflownPixels) + "px";
                }

                var closeSubMenus = function () {
                    var childSubMenus = findAllChildsByClass(currentItemElement, "blazor-context-submenu");
                    var i = childSubMenus.length;
                    while (i--) {
                        var subMenu = childSubMenus[i];
                        blazorContextMenu.Hide(subMenu.id);
                    }

                    i = currentMenuList.childNodes.length;
                    while (i--) {
                        var child = currentMenuList.children[i];
                        if (child == currentItemElement) continue;
                        child.removeEventListener("mouseover", closeSubMenus);
                    }
                };

                var i = currentMenuList.childNodes.length;
                while (i--) {
                    var child = currentMenuList.childNodes[i];
                    if (child == currentItemElement) continue;

                    child.addEventListener("mouseover", closeSubMenus);
                }
            });
        }, 200);
    }

    blazorContextMenu.OnMenuItemMouseOut = function (e) {
        if (subMenuTimeout) {
            clearTimeout(subMenuTimeout);
        }
    }

    return blazorContextMenu;
}({});

blazorContextMenu.Init();

export { blazorContextMenu }
