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
        var triggerDotnetRef = JSON.parse(e.currentTarget.dataset["dotnetref"]);
        showMenuCommon(menu, menuId, e.x, e.y, e.target, triggerDotnetRef);
        e.preventDefault();
        if (stopPropagation) {
            e.stopPropagation();
        }
        return false;
    };

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
        const placement = target.getElementsByClassName('placement');
        let x = e.x;
        let y = e.y;
        if (placement && placement.length > 0) {
            const rect = placement[0].getBoundingClientRect();
            x = rect.left;
            y = rect.top;
        }
        addToOpenMenus(menu, menuId, e.target, target);
        const triggerDotnetRef = JSON.parse(e.currentTarget.dataset["dotnetref"]);
        showMenuCommon(menu, menuId, x, y, e.target, triggerDotnetRef);
        e.preventDefault();
        if (stopPropagation) {
            e.stopPropagation();
        }
        return false;
    };

    var showMenuCommon = function (menu, menuId, x, y, target, triggerDotnetRef) {
        return blazorContextMenu.Show(menuId, x, y, target, triggerDotnetRef).then(function () {
            //check for overflow
            var leftOverflownPixels = menu.offsetLeft + menu.clientWidth - window.innerWidth;
            if (leftOverflownPixels > 0) {
                menu.style.left = (menu.offsetLeft - menu.clientWidth) + "px";
            }

            var topOverflownPixels = menu.offsetTop + menu.clientHeight - window.innerHeight;
            if (topOverflownPixels > 0) {
                menu.style.top = (menu.offsetTop - menu.clientHeight) + "px";
            }

            //openingMenu = false;
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
                            var mustClose = false;
                            var clickedInsideMenu = menuElement.contains(e.target);
                            var clickedInsideToggle = currentMenu.toggle && currentMenu.toggle.contains(e.target);
                            var clickedMenuItem = e.target.closest('.blazor-context-menu__item');
                            if (!(clickedInsideMenu || clickedInsideToggle))
                                mustClose = true;
                            else if (clickedInsideMenu && !clickedMenuItem.classList.contains('.blazor-context-menu__item--with-submenu'))
                                mustClose = true;
                            if (mustClose) {
                                setTimeout(() => {
                                    blazorContextMenu.Hide(currentMenu.id);
                                }, 200)
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


    blazorContextMenu.RegisterTriggerReference = function (triggerElement, triggerDotNetRef) {
        if (triggerElement) {
            triggerElement.dataset["dotnetref"] = JSON.stringify(triggerDotNetRef.serializeAsArg());
        }
    }

    return blazorContextMenu;
}({});

blazorContextMenu.Init();
